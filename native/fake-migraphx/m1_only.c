#include <stdlib.h>

#if defined(_WIN32)
#define EXPORT __declspec(dllexport)
#else
#define EXPORT __attribute__((visibility("default")))
#endif

typedef struct fake_object { int value; } *migraphx_target_t, *migraphx_program_t;

EXPORT int migraphx_target_destroy(migraphx_target_t value) { free(value); return 0; }
EXPORT int migraphx_target_assign_to(migraphx_target_t output, const migraphx_target_t input) { if(output == NULL || input == NULL) return 1; *output = *input; return 0; }
EXPORT int migraphx_target_create(migraphx_target_t* value, const char* name) { if(value == NULL || name == NULL) return 1; *value = (migraphx_target_t)calloc(1, sizeof(**value)); return *value == NULL ? 4 : 0; }
EXPORT int migraphx_program_destroy(migraphx_program_t value) { free(value); return 0; }
EXPORT int migraphx_program_assign_to(migraphx_program_t output, const migraphx_program_t input) { if(output == NULL || input == NULL) return 1; *output = *input; return 0; }
EXPORT int migraphx_program_create(migraphx_program_t* value) { if(value == NULL) return 1; *value = (migraphx_program_t)calloc(1, sizeof(**value)); return *value == NULL ? 4 : 0; }
