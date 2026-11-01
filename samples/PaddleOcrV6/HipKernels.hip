#if !defined(__HIPCC_RTC__)
#include <hip/hip_runtime.h>
#include <math.h>
#else
#define INFINITY __builtin_huge_valf()
#endif

// All images are packed RGB bytes. Resize uses separable, antialiased triangle
// or Catmull-Rom cubic filtering; normalization and layout conversion stay on GPU.
__device__ float farg(int bits) { return __int_as_float(bits); }
__device__ int ci(int x, int lo, int hi) { return x < lo ? lo : (x > hi ? hi : x); }
__device__ double cd(double x, double lo, double hi) { return fmin(hi, fmax(lo, x)); }
__device__ float weight(float x, int cubic) {
    x = fabsf(x);
    if (!cubic) return fmaxf(0.f, 1.f - x);
    if (x < 1.f) return (1.5f*x - 2.5f)*x*x + 1.f;
    return x < 2.f ? ((-.5f*x + 2.5f)*x - 4.f)*x + 2.f : 0.f;
}
extern "C" __global__ void resize_x(const unsigned char* src, float* dst, int sw, int sh,
    int ox, int oy, int cw, int ch, int dw, int cubic) {
    int i = blockIdx.x*blockDim.x + threadIdx.x;
    if (i >= dw*ch*3) return;
    int c=i%3, x=(i/3)%dw, y=i/(dw*3);
    float scale=(float)cw/dw, filter=fmaxf(1.f,scale), center=(x+.5f)*scale-.5f;
    float radius=filter*(cubic?2.f:1.f), sum=0, total=0;
    int first=max(0,(int)ceilf(center-radius)), last=min(cw-1,(int)floorf(center+radius));
    for (int sx=first;sx<=last;sx++) {
        float w=weight((sx-center)/filter,cubic);
        sum += src[((y+oy)*sw+sx+ox)*3+c]*w; total+=w;
    }
    dst[i]=sum/total;
}
extern "C" __global__ void resize_y(const float* src, unsigned char* dst, int w, int sh, int dh, int cubic) {
    int i=blockIdx.x*blockDim.x+threadIdx.x;
    if(i>=w*dh*3) return;
    int c=i%3,x=(i/3)%w,y=i/(w*3);
    float scale=(float)sh/dh,filter=fmaxf(1.f,scale),center=(y+.5f)*scale-.5f;
    float radius=filter*(cubic?2.f:1.f),sum=0,total=0;
    for(int sy=max(0,(int)ceilf(center-radius));sy<=min(sh-1,(int)floorf(center+radius));sy++){
        float a=weight((sy-center)/filter,cubic);sum+=src[(sy*w+x)*3+c]*a;total+=a;
    }
    dst[i]=(unsigned char)ci((int)floorf(sum/total+.5f),0,255);
}
extern "C" __global__ void tensorize(const unsigned char* src, float* dst, int rw,int rh,int w,int h,
    int left,int top,int mode) {
    int i=blockIdx.x*blockDim.x+threadIdx.x;
    if(i>=w*h*3)return;
    int c=i/(w*h),p=i%(w*h),x=p%w-left,y=p/w-top;
    bool inside=x>=0&&y>=0&&x<rw&&y<rh;
    int channel=mode==0?c:2-c;
    float value=inside?src[(y*rw+x)*3+channel]/255.f:(mode==0?114.f/255.f:0.f);
    if(mode==1){const float mean[]={.485f,.456f,.406f},sd[]={.229f,.224f,.225f};value=(value-mean[c])/sd[c];}
    if(mode==2)value=inside?(value-.5f)/.5f:0.f;
    dst[i]=value;
}
extern "C" __global__ void clear_int(int* dst,int n,int value){
    int i=blockIdx.x*blockDim.x+threadIdx.x;if(i<n)dst[i]=value;
}
extern "C" __global__ void clear_float(float* dst,int n){
    int i=blockIdx.x*blockDim.x+threadIdx.x;if(i<n)dst[i]=0;
}

// DB connected components: monotonic union-find on all 8-connected foreground edges.
__device__ int root(int* parents,int x){int p;while((p=atomicAdd(parents+x,0))!=x)x=p;return x;}
__device__ void unite(int* parents,int a,int b){
    while(true){a=root(parents,a);b=root(parents,b);if(a==b)return;
        int hi=max(a,b),lo=min(a,b);if(atomicCAS(parents+hi,hi,lo)==hi)return;}
}
extern "C" __global__ void db_init(const float* map,int* parents,int* counts,int n,int thresholdBits,int* error){
    int i=blockIdx.x*blockDim.x+threadIdx.x;if(i>=n)return;
    float p=map[i];if(!isfinite(p)||p<0||p>1)atomicExch(error,1);
    parents[i]=p>farg(thresholdBits)?i:-1;counts[i]=0;
}
extern "C" __global__ void db_union(int* parents,int w,int h){
    int i=blockIdx.x*blockDim.x+threadIdx.x;if(i>=w*h||parents[i]<0)return;
    int x=i%w,y=i/w;
    if(x>0&&parents[i-1]>=0)unite(parents,i,i-1);
    if(y>0)for(int dx=-1;dx<=1;dx++)if(x+dx>=0&&x+dx<w&&parents[i-w+dx]>=0)unite(parents,i,i-w+dx);
}
__device__ bool boundary(const float* map,int i,int w,int h,float threshold){
    int x=i%w,y=i/w;
    for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++){
        if(dx==0&&dy==0)continue;int nx=x+dx,ny=y+dy;
        if(nx<0||ny<0||nx>=w||ny>=h||map[ny*w+nx]<=threshold)return true;
    }return false;
}
extern "C" __global__ void db_count(const float* map,int* parents,int* counts,int w,int h,int thresholdBits){
    int i=blockIdx.x*blockDim.x+threadIdx.x;if(i>=w*h||parents[i]<0)return;
    int r=root(parents,i);atomicExch(parents+i,r);if(boundary(map,i,w,h,farg(thresholdBits)))atomicAdd(counts+r,1);
}
// Deterministic two-level prefix scan: parallel pixels, then only one pair of
// totals per 256-pixel block on the serial path. Root IDs retain pixel order.
extern "C" __global__ void db_scan_local(const int* parents,const int* counts,int* components,int* pointOffsets,
    int* blockRoots,int* blockPoints,int n){
    int i=blockIdx.x*blockDim.x+threadIdx.x,t=threadIdx.x;
    int isRoot=i<n&&parents[i]==i,points=isRoot?counts[i]:0;
    __shared__ int roots[256],sizes[256];roots[t]=isRoot;sizes[t]=points;__syncthreads();
    for(int stride=1;stride<256;stride*=2){
        int a=t>=stride?roots[t-stride]:0,b=t>=stride?sizes[t-stride]:0;
        __syncthreads();roots[t]+=a;sizes[t]+=b;__syncthreads();
    }
    if(i<n){components[i]=isRoot?roots[t]-1:-1;pointOffsets[i]=sizes[t]-points;}
    if(t==255){blockRoots[blockIdx.x]=roots[t];blockPoints[blockIdx.x]=sizes[t];}
}
extern "C" __global__ void db_scan_blocks(int* blockRoots,int* blockPoints,int blocks,int* control){
    if(blockIdx.x||threadIdx.x)return;int roots=0,points=0;
    for(int b=0;b<blocks;b++){int r=blockRoots[b],p=blockPoints[b];blockRoots[b]=roots;blockPoints[b]=points;roots+=r;points+=p;}
    control[0]=min(roots,3000);if(roots>3000)control[1]=2;
}
extern "C" __global__ void db_scan_finalize(int* counts,int* components,const int* pointOffsets,const int* blockRoots,
    const int* blockPoints,int* offsets,int* sizes,int n){
    int i=blockIdx.x*blockDim.x+threadIdx.x;if(i>=n||components[i]<0)return;
    int c=components[i]+blockRoots[blockIdx.x];components[i]=c<3000?c:-1;
    if(c<3000){offsets[c]=pointOffsets[i]+blockPoints[blockIdx.x];sizes[c]=counts[i];counts[i]=0;}
}
extern "C" __global__ void db_gather(const float* map,const int* parents,int* counts,const int* components,
    const int* offsets,int* points,int w,int h,int thresholdBits){
    int i=blockIdx.x*blockDim.x+threadIdx.x;if(i>=w*h||parents[i]<0)return;
    int r=parents[i],c=components[r];if(c<0||!boundary(map,i,w,h,farg(thresholdBits)))return;
    points[offsets[c]+atomicAdd(counts+r,1)]=i;
}
struct Point {double x,y;};
__device__ Point point(int i,int w){return {(double)(i%w),(double)(i/w)};}
__device__ double cross(Point a,Point b,Point c){return (b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x);}
__device__ double dist2(Point a,Point b){double x=a.x-b.x,y=a.y-b.y;return x*x+y*y;}
__device__ void order_quad(Point* q){
    for(int i=1;i<4;i++)for(int j=i;j>0&&(q[j].x<q[j-1].x||(q[j].x==q[j-1].x&&q[j].y<q[j-1].y));j--){Point t=q[j];q[j]=q[j-1];q[j-1]=t;}
    if(q[0].y>q[1].y){Point t=q[0];q[0]=q[1];q[1]=t;}
    if(q[2].y>q[3].y){Point t=q[2];q[2]=q[3];q[3]=t;}
    Point t=q[1];q[1]=q[2];q[2]=q[3];q[3]=t;
}
// One thread per component constructs its exact convex hull and minimum-area rectangle.
// Irregular contour work remains on the GPU; all dense image operations are parallel kernels.
extern "C" __global__ void db_rectangles(const float* map,const int* points,int* hull,const int* offsets,const int* sizes,
    const int* control,double* boxes,int w,int h,int sw,int sh,int scoreBits){
    int c=blockIdx.x*blockDim.x+threadIdx.x;if(c>=control[0])return;
    double* box=boxes+c*9;box[8]=-1;int n=sizes[c],o=offsets[c];if(n<3)return;
    int start=0;for(int j=1;j<n;j++){Point a=point(points[o+j],w),b=point(points[o+start],w);if(a.x<b.x||(a.x==b.x&&a.y<b.y))start=j;}
    int current=start,hc=0;
    do{
        if(hc>=n)return;hull[o+hc++]=points[o+current];int next=current==0?1:0;
        for(int j=0;j<n;j++){
            Point a=point(points[o+current],w),b=point(points[o+next],w),p=point(points[o+j],w);
            double turn=cross(a,b,p);if(turn<0||(turn==0&&dist2(a,p)>dist2(a,b)))next=j;
        }
        current=next;
    }while(current!=start);
    if(hc<3)return;
    double area=INFINITY,bux=0,buy=0,bx0=0,bx1=0,by0=0,by1=0;
    for(int j=0;j<hc;j++){
        Point a=point(hull[o+j],w),b=point(hull[o+(j+1)%hc],w);double len=sqrt(dist2(a,b));if(len<1e-8)continue;
        double ux=(b.x-a.x)/len,uy=(b.y-a.y)/len,x0=INFINITY,x1=-INFINITY,y0=INFINITY,y1=-INFINITY;
        for(int k=0;k<hc;k++){Point p=point(hull[o+k],w);double x=p.x*ux+p.y*uy,y=-p.x*uy+p.y*ux;x0=fmin(x0,x);x1=fmax(x1,x);y0=fmin(y0,y);y1=fmax(y1,y);}
        double value=(x1-x0)*(y1-y0);if(value<area){area=value;bux=ux;buy=uy;bx0=x0;bx1=x1;by0=y0;by1=y1;}
    }
    double bw=bx1-bx0,bh=by1-by0;if(fmin(bw,bh)<3)return;
    Point q[4]={{bx0*bux-by0*buy,bx0*buy+by0*bux},{bx1*bux-by0*buy,bx1*buy+by0*bux},
        {bx1*bux-by1*buy,bx1*buy+by1*bux},{bx0*bux-by1*buy,bx0*buy+by1*bux}};
    double minx=q[0].x,maxx=minx,miny=q[0].y,maxy=miny;
    for(int k=1;k<4;k++){minx=fmin(minx,q[k].x);maxx=fmax(maxx,q[k].x);miny=fmin(miny,q[k].y);maxy=fmax(maxy,q[k].y);}
    double sum=0;int samples=0;
    for(int y=ci((int)floor(miny),0,h-1);y<=ci((int)ceil(maxy),0,h-1);y++)
    for(int x=ci((int)floor(minx),0,w-1);x<=ci((int)ceil(maxx),0,w-1);x++){
        bool positive=false,negative=false;Point p={(double)x,(double)y};
        for(int k=0;k<4;k++){double cr=cross(q[k],q[(k+1)%4],p);positive|=cr>1e-7;negative|=cr< -1e-7;}
        if(!(positive&&negative)){sum+=map[y*w+x];samples++;}
    }
    double score=samples?sum/samples:0;if(score<farg(scoreBits))return;
    double expand=bw*bh*1.4/(2*(bw+bh)),cx=(bx0+bx1)/2,cy=(by0+by1)/2;
    for(int k=0;k<4;k++){
        double x=cx+((k==0||k==3)?-1:1)*(bw/2+expand),y=cy+(k<2?-1:1)*(bh/2+expand);
        q[k]={cd((x*bux-y*buy)*sw/w,0,sw-1),cd((x*buy+y*bux)*sh/h,0,sh-1)};
    }
    order_quad(q);for(int k=0;k<4;k++){box[k*2]=q[k].x;box[k*2+1]=q[k].y;}box[8]=score;
}
extern "C" __global__ void db_sort(const double* boxes,double* sorted,const int* control,int* order,int* resultCount){
    if(blockIdx.x||threadIdx.x)return;int n=0;
    for(int i=0;i<control[0];i++)if(boxes[i*9+8]>=0){
        int j=n;while(j>0){int b=order[j-1];if(boxes[b*9+1]<boxes[i*9+1]||(boxes[b*9+1]==boxes[i*9+1]&&boxes[b*9]<=boxes[i*9]))break;order[j]=b;j--;}
        order[j]=i;n++;
    }
    for(int i=1;i<n;i++)for(int j=i;j>0&&fabs(boxes[order[j]*9+1]-boxes[order[j-1]*9+1])<10&&boxes[order[j]*9]<boxes[order[j-1]*9];j--){int t=order[j];order[j]=order[j-1];order[j-1]=t;}
    for(int i=0;i<n;i++)for(int j=0;j<9;j++)sorted[i*9+j]=boxes[order[i]*9+j];*resultCount=n;
}
extern "C" __global__ void crop_meta(const double* boxes,int line,int* meta,int recWidth,int maxEdge,int* error){
    if(blockIdx.x||threadIdx.x)return;meta+=line*6;const double* q=boxes+line*9;
    Point a={q[0],q[1]},b={q[2],q[3]},c={q[4],q[5]},d={q[6],q[7]};
    int w=ci((int)nearbyint(sqrt(fmax(dist2(a,b),dist2(c,d)))),1,8192),h=ci((int)nearbyint(sqrt(fmax(dist2(a,d),dist2(b,c)))),1,8192);
    int rotate=(double)h/w>=1.5;meta[0]=w;meta[1]=h;meta[2]=rotate?h:w;meta[3]=rotate?w:h;meta[4]=rotate;
    meta[5]=ci((int)ceil(48.*meta[2]/meta[3]),1,recWidth);if(w>maxEdge||h>maxEdge)atomicExch(error,1);
}
__device__ unsigned char sample_byte(const unsigned char* src,int w,int h,double x,double y,int c){
    x=cd(x,0,w-1);y=cd(y,0,h-1);int x0=(int)x,y0=(int)y,x1=min(x0+1,w-1),y1=min(y0+1,h-1);double fx=x-x0,fy=y-y0;
    double a=src[(y0*w+x0)*3+c]*(1-fx)+src[(y0*w+x1)*3+c]*fx,b=src[(y1*w+x0)*3+c]*(1-fx)+src[(y1*w+x1)*3+c]*fx;
    return (unsigned char)ci((int)nearbyint(a*(1-fy)+b*fy),0,255);
}
extern "C" __global__ void perspective_crop(const unsigned char* src,unsigned char* dst,int sw,int sh,const double* boxes,int line,const int* meta){
    meta+=line*6;int i=blockIdx.x*blockDim.x+threadIdx.x,ow=meta[2],oh=meta[3];if(i>=ow*oh*3)return;
    int channel=i%3,xx=(i/3)%ow,yy=i/(ow*3),x=meta[4]?meta[0]-1-yy:xx,y=meta[4]?xx:yy;
    double u=meta[0]==1?0:(double)x/(meta[0]-1),v=meta[1]==1?0:(double)y/(meta[1]-1);const double* q=boxes+line*9;
    double dx1=q[2]-q[4],dx2=q[6]-q[4],dy1=q[3]-q[5],dy2=q[7]-q[5],sx=q[0]-q[2]+q[4]-q[6],sy=q[1]-q[3]+q[5]-q[7];
    double det=dx1*dy2-dx2*dy1,g=fabs(det)<1e-8?0:(sx*dy2-dx2*sy)/det,h=fabs(det)<1e-8?0:(dx1*sy-sx*dy1)/det;
    double a=q[2]-q[0]+g*q[2],b=q[6]-q[0]+h*q[6],d=q[3]-q[1]+g*q[3],e=q[7]-q[1]+h*q[7],div=g*u+h*v+1;
    dst[i]=sample_byte(src,sw,sh,(a*u+b*v+q[0])/div,(d*u+e*v+q[1])/div,channel);
}
extern "C" __global__ void rec_resize_x(const unsigned char* src,float* dst,const int* meta,int line){
    meta+=line*6;int i=blockIdx.x*blockDim.x+threadIdx.x,w=meta[2],h=meta[3],dw=meta[5];if(i>=dw*h*3)return;
    int c=i%3,x=(i/3)%dw,y=i/(dw*3);float scale=(float)w/dw,filter=fmaxf(1.f,scale),center=(x+.5f)*scale-.5f,sum=0,total=0;
    for(int sx=max(0,(int)ceilf(center-filter));sx<=min(w-1,(int)floorf(center+filter));sx++){float a=weight((sx-center)/filter,0);sum+=src[(y*w+sx)*3+c]*a;total+=a;}dst[i]=sum/total;
}
extern "C" __global__ void rec_resize_tensor(const float* src,float* dst,const int* meta,int width,int line,int slot){
    dst+=slot*width*48*3;
    meta+=line*6;int i=blockIdx.x*blockDim.x+threadIdx.x;if(i>=width*48*3)return;
    int c=i/(width*48),p=i%(width*48),x=p%width,y=p/width,w=meta[5],h=meta[3];if(x>=w){dst[i]=0;return;}
    float scale=(float)h/48,filter=fmaxf(1.f,scale),center=(y+.5f)*scale-.5f,sum=0,total=0;
    for(int sy=max(0,(int)ceilf(center-filter));sy<=min(h-1,(int)floorf(center+filter));sy++){float a=weight((sy-center)/filter,0);sum+=src[(sy*w+x)*3+2-c]*a;total+=a;}
    float value=(float)ci((int)floorf(sum/total+.5f),0,255);dst[i]=(value/255.f-.5f)/.5f;
}
extern "C" __global__ void ctc_argmax(const float* data,int classes,int steps,int batch,int* ids,float* scores,int* error){
    int t=blockIdx.x,tid=threadIdx.x;if(t>=steps*batch)return;__shared__ float values[256];__shared__ int indices[256];
    float best=-INFINITY;int id=0;
    for(int c=tid;c<classes;c+=256){float v=data[t*classes+c];if(!isfinite(v))atomicExch(error,1);if(v>best){best=v;id=c;}}
    values[tid]=best;indices[tid]=id;__syncthreads();
    for(int stride=128;stride;stride/=2){if(tid<stride&&(values[tid+stride]>values[tid]||(values[tid+stride]==values[tid]&&indices[tid+stride]<indices[tid]))){values[tid]=values[tid+stride];indices[tid]=indices[tid+stride];}__syncthreads();}
    if(tid==0){ids[t]=indices[0];scores[t]=values[0];if(values[0]<0||values[0]>1)atomicExch(error,1);}
}
extern "C" __global__ void ctc_compact(const int* ids,const float* scores,int steps,int firstLine,int batch,int* tokens,float* confidence){
    int slot=blockIdx.x*blockDim.x+threadIdx.x;if(slot>=batch)return;int line=firstLine+slot;
    ids+=slot*steps;scores+=slot*steps;
    int n=0,previous=-1;double sum=0;int* out=tokens+line*(steps+1);
    for(int t=0;t<steps;t++){int id=ids[t];if(id!=0&&id!=previous){out[1+n++]=id;sum+=scores[t];}previous=id;}
    out[0]=n;confidence[line]=n?(float)(sum/n):0;
}

