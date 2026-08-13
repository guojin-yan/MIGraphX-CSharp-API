#if defined(_WIN32)
#define EXPORT __declspec(dllexport)
#else
#define EXPORT __attribute__((visibility("default")))
#endif

EXPORT int migraphx_target_create(void** target, const char* name)
{
    (void)target;
    (void)name;
    return 0;
}
