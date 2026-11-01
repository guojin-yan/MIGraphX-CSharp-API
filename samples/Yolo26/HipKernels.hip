#include <hip/hip_runtime.h>
#include <math.h>

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

extern "C" __global__ void check_finite(const float* data,int n,int* error){
    int i=blockIdx.x*blockDim.x+threadIdx.x;if(i<n&&!isfinite(data[i]))atomicExch(error,1);
}
extern "C" __global__ void classify_top(const float* data,int n,float* top,int* error){
    int i=blockIdx.x*blockDim.x+threadIdx.x;if(i>=n)return;
    float score=data[i];if(!isfinite(score)||score<0||score>1){atomicExch(error,1);return;}
    int rank=0;for(int j=0;j<n;j++)if(data[j]>score||(data[j]==score&&j<i))rank++;
    if(rank<5){top[rank*2]=(float)i;top[rank*2+1]=score;}
}
// Compact records: class, score, polygon[8], keypoints[51]. All coordinates are restored on GPU.
extern "C" __global__ void yolo_select(const float* data,int n,int fields,int task,int maximum,int thresholdBits,
    int iw,int ih,int rw,int rh,int left,int top,int sw,int sh,int* selected,float* records,int* count,int* error){
    int i=blockIdx.x*blockDim.x+threadIdx.x;if(i>=n)return;
    const float* p=data+i*fields;float score=p[4];
    if(!isfinite(score)||score<0||score>1||!isfinite(p[5])||p[5]<0||p[5]!=truncf(p[5])){atomicExch(error,1);return;}
    if(score<farg(thresholdBits))return;
    int rank=0;for(int j=0;j<n;j++){float s=data[j*fields+4];if(s>score||(s==score&&j<i))rank++;}
    if(rank>=maximum)return;
    selected[rank]=i;atomicMax(count,rank+1);float* out=records+rank*61;out[0]=p[5];out[1]=score;
    double scale=fmin((double)iw/sw,(double)ih/sh);
    for(int k=0;k<4;k++){
        double x,y;
        if(task==3){double u=(k==0||k==3)?-.5:.5,v=k<2?-.5:.5;
            x=p[0]+u*p[2]*cos((double)p[6])-v*p[3]*sin((double)p[6]);
            y=p[1]+u*p[2]*sin((double)p[6])+v*p[3]*cos((double)p[6]);}
        else{x=p[(k==0||k==3)?0:2];y=p[k<2?1:3];}
        out[2+k*2]=(float)cd((x-left)/scale,0,sw-1);out[3+k*2]=(float)cd((y-top)/scale,0,sh-1);
    }
    if(task==2)for(int k=0;k<17;k++){
        out[10+k*3]=(float)cd((p[6+k*3]-left)/scale,0,sw-1);
        out[11+k*3]=(float)cd((p[7+k*3]-top)/scale,0,sh-1);
        float s=p[8+k*3];out[12+k*3]=s;if(s<0||s>1)atomicExch(error,1);
    }
}
extern "C" __global__ void mask_logits(const float* data,const float* proto,const int* selected,const int* count,float* logits,int fields){
    int i=blockIdx.x*blockDim.x+threadIdx.x;if(i>=*count*160*160)return;
    int instance=i/(160*160),p=i%(160*160),offset=selected[instance]*fields;
    float value=0;for(int c=0;c<32;c++)value+=data[offset+6+c]*proto[c*160*160+p];
    logits[i]=value;
}
__device__ float cropped_logit(const float* logits,int x,int y,const float* box,int iw,int ih){
    if(x<box[0]*160/iw||x>=box[2]*160/iw||y<box[1]*160/ih||y>=box[3]*160/ih)return 0;
    return logits[y*160+x];
}
extern "C" __global__ void mask_restore(const float* data,const float* logits,const int* selected,const int* count,
    int fields,int iw,int ih,int sw,int sh,int left,int top,unsigned char* masks){
    int i=blockIdx.x*blockDim.x+threadIdx.x;if(i>=*count*sw*sh)return;
    int instance=i/(sw*sh),p=i%(sw*sh),x=p%sw,y=p/sw;
    double scale=fmin((double)iw/sw,(double)ih/sh);
    double mx=cd(((x+.5)*scale+left)*160/iw-.5,0,159),my=cd(((y+.5)*scale+top)*160/ih-.5,0,159);
    int x0=(int)mx,y0=(int)my,x1=min(x0+1,159),y1=min(y0+1,159);double fx=mx-x0,fy=my-y0;
    const float* l=logits+instance*160*160;const float* b=data+selected[instance]*fields;
    double value=(cropped_logit(l,x0,y0,b,iw,ih)*(1-fx)+cropped_logit(l,x1,y0,b,iw,ih)*fx)*(1-fy)
        +(cropped_logit(l,x0,y1,b,iw,ih)*(1-fx)+cropped_logit(l,x1,y1,b,iw,ih)*fx)*fy;
    masks[i]=value>0?255:0;
}
extern "C" __global__ void mask_count(const unsigned char* masks,const int* count,int plane,int* pixels){
    int instance=blockIdx.x,tid=threadIdx.x;if(instance>=*count)return;
    __shared__ int sums[256];int sum=0;
    for(int p=tid;p<plane;p+=256)sum+=masks[instance*plane+p]!=0;
    sums[tid]=sum;__syncthreads();
    for(int stride=128;stride;stride/=2){if(tid<stride)sums[tid]+=sums[tid+stride];__syncthreads();}
    if(tid==0)pixels[instance]=sums[0];
}

