#include <stdint.h>
#include <stdlib.h>
#include <string.h>

#if defined(_WIN32)
#define EXPORT __declspec(dllexport)
#else
#define EXPORT __attribute__((visibility("default")))
#endif

typedef struct fake_stream { int value; } fake_stream;
static int query_status;
static int current_device;
static int stream_destroy_count;
static int free_count;
static int memcpy_count;
static int memcpy_status;
static int next_stream = 1;

EXPORT void fake_hip_reset(void)
{
    query_status = 600;
    current_device = 0;
    stream_destroy_count = 0;
    free_count = 0;
    memcpy_count = 0;
    memcpy_status = 0;
}
EXPORT void fake_hip_set_query_status(int status) { query_status = status; }
EXPORT void fake_hip_set_device(int device) { current_device = device; }
EXPORT void fake_hip_set_memcpy_status(int status) { memcpy_status = status; }
EXPORT int fake_hip_stream_destroy_count(void) { return stream_destroy_count; }
EXPORT int fake_hip_free_count(void) { return free_count; }
EXPORT int fake_hip_memcpy_count(void) { return memcpy_count; }

EXPORT int hipInit(unsigned int flags) { (void)flags; return 0; }
EXPORT int hipGetDevice(int* device) { if(device == NULL) return 1; *device = current_device; return 0; }
EXPORT int hipSetDevice(int device) { current_device = device; return 0; }
EXPORT int hipStreamCreateWithFlags(void** stream, unsigned int flags)
{
    fake_stream* value;
    (void)flags;
    if(stream == NULL) return 1;
    value = (fake_stream*)malloc(sizeof(fake_stream));
    if(value == NULL) return 2;
    value->value = next_stream++;
    *stream = value;
    return 0;
}
EXPORT int hipStreamDestroy(void* stream)
{
    if(stream == NULL) return 1;
    free(stream);
    stream_destroy_count++;
    return 0;
}
EXPORT int hipStreamSynchronize(void* stream) { return stream == NULL ? 1 : 0; }
EXPORT int hipStreamQuery(void* stream) { return stream == NULL ? 1 : query_status; }
EXPORT int hipStreamBeginCapture(void* stream, int mode) { (void)mode; return stream == NULL ? 1 : 0; }
EXPORT int hipStreamEndCapture(void* stream, void** graph) { if(stream == NULL || graph == NULL) return 1; *graph = malloc(1); return *graph == NULL ? 2 : 0; }
EXPORT int hipGraphDestroy(void* graph) { if(graph == NULL) return 1; free(graph); return 0; }
EXPORT int hipMalloc(void** pointer, size_t bytes)
{
    if(pointer == NULL || bytes == 0) return 1;
    *pointer = malloc(bytes);
    return *pointer == NULL ? 2 : 0;
}
EXPORT int hipFree(void* pointer) { if(pointer == NULL) return 1; free(pointer); free_count++; return 0; }
EXPORT int hipMemcpy(void* destination, const void* source, size_t bytes, int kind)
{
    (void)kind;
    if(destination == NULL || source == NULL) return 1;
    if(memcpy_status != 0) return memcpy_status;
    memcpy(destination, source, bytes);
    memcpy_count++;
    return 0;
}
