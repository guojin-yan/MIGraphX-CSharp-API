#include <stdint.h>
#include <stdlib.h>
#include <string.h>

#if defined(_WIN32)
#include <windows.h>
#define EXPORT __declspec(dllexport)
#define ATOMIC_INCREMENT(value) InterlockedIncrement((volatile LONG*)&(value))
#define ATOMIC_DECREMENT(value) InterlockedDecrement((volatile LONG*)&(value))
#define ATOMIC_EXCHANGE(value, replacement) InterlockedExchange((volatile LONG*)&(value), (replacement))
#else
#define EXPORT __attribute__((visibility("default")))
#define ATOMIC_INCREMENT(value) __atomic_add_fetch(&(value), 1, __ATOMIC_SEQ_CST)
#define ATOMIC_DECREMENT(value) __atomic_sub_fetch(&(value), 1, __ATOMIC_SEQ_CST)
#define ATOMIC_EXCHANGE(value, replacement) __atomic_exchange_n(&(value), (replacement), __ATOMIC_SEQ_CST)
#endif

typedef enum
{
    migraphx_status_success        = 0,
    migraphx_status_bad_param      = 1,
    migraphx_status_unknown_target = 3,
    migraphx_status_unknown_error  = 4
} migraphx_status;

typedef struct fake_object
{
    int value;
} *migraphx_target_t, *migraphx_program_t;

static volatile int32_t next_status;
static volatile int32_t create_null;
static volatile int32_t target_destroy_count;
static volatile int32_t program_destroy_count;
static volatile int32_t target_assign_count;
static volatile int32_t program_assign_count;
static volatile int32_t target_live_count;
static volatile int32_t program_live_count;
static volatile int32_t target_assign_copied;
static volatile int32_t program_assign_copied;
static volatile int32_t next_value = 100;
static volatile int32_t target_name_lock;
static char last_target_name[512];

static int take_status(void)
{
    return ATOMIC_EXCHANGE(next_status, 0);
}

static void lock_target_name(void)
{
    while(ATOMIC_EXCHANGE(target_name_lock, 1) != 0)
    {
    }
}

static void unlock_target_name(void)
{
    ATOMIC_EXCHANGE(target_name_lock, 0);
}

EXPORT void fake_reset(void)
{
    next_status = 0;
    create_null = 0;
    target_destroy_count = 0;
    program_destroy_count = 0;
    target_assign_count = 0;
    program_assign_count = 0;
    target_live_count = 0;
    program_live_count = 0;
    target_assign_copied = 0;
    program_assign_copied = 0;
    next_value = 100;
    target_name_lock = 0;
    last_target_name[0] = '\0';
}

EXPORT void fake_set_next_status(int status) { ATOMIC_EXCHANGE(next_status, status); }
EXPORT void fake_set_create_null(int enabled) { ATOMIC_EXCHANGE(create_null, enabled); }
EXPORT int fake_target_destroy_count(void) { return target_destroy_count; }
EXPORT int fake_program_destroy_count(void) { return program_destroy_count; }
EXPORT int fake_target_assign_count(void) { return target_assign_count; }
EXPORT int fake_program_assign_count(void) { return program_assign_count; }
EXPORT int fake_target_live_count(void) { return target_live_count; }
EXPORT int fake_program_live_count(void) { return program_live_count; }
EXPORT int fake_target_assign_copied(void) { return target_assign_copied; }
EXPORT int fake_program_assign_copied(void) { return program_assign_copied; }
EXPORT int fake_sizeof_status(void) { return (int)sizeof(migraphx_status); }
EXPORT int fake_sizeof_target_handle(void) { return (int)sizeof(migraphx_target_t); }
EXPORT const char* fake_last_target_name(void) { return last_target_name; }

EXPORT migraphx_status migraphx_target_destroy(migraphx_target_t target)
{
    if(target != NULL)
    {
        free(target);
        ATOMIC_INCREMENT(target_destroy_count);
        ATOMIC_DECREMENT(target_live_count);
    }
    return (migraphx_status)take_status();
}

EXPORT migraphx_status migraphx_target_assign_to(migraphx_target_t output, const migraphx_target_t input)
{
    ATOMIC_INCREMENT(target_assign_count);
    if(output == NULL || input == NULL)
        return migraphx_status_bad_param;
    output->value = input->value;
    target_assign_copied = output->value == input->value;
    return (migraphx_status)take_status();
}

EXPORT migraphx_status migraphx_target_create(migraphx_target_t* target, const char* name)
{
    int status;
    size_t name_length;
    if(target == NULL || name == NULL)
        return migraphx_status_bad_param;
    name_length = strlen(name);
    if(name_length >= sizeof(last_target_name))
        name_length = sizeof(last_target_name) - 1;
    lock_target_name();
    memcpy(last_target_name, name, name_length);
    last_target_name[name_length] = '\0';
    unlock_target_name();
    if(ATOMIC_EXCHANGE(create_null, 0))
    {
        *target = NULL;
    }
    else
    {
        *target = (migraphx_target_t)malloc(sizeof(**target));
        if(*target == NULL)
            return migraphx_status_unknown_error;
        (*target)->value = ATOMIC_INCREMENT(next_value);
        ATOMIC_INCREMENT(target_live_count);
    }
    status = take_status();
    return (migraphx_status)status;
}

EXPORT migraphx_status migraphx_program_destroy(migraphx_program_t program)
{
    if(program != NULL)
    {
        free(program);
        ATOMIC_INCREMENT(program_destroy_count);
        ATOMIC_DECREMENT(program_live_count);
    }
    return (migraphx_status)take_status();
}

EXPORT migraphx_status migraphx_program_assign_to(migraphx_program_t output, const migraphx_program_t input)
{
    ATOMIC_INCREMENT(program_assign_count);
    if(output == NULL || input == NULL)
        return migraphx_status_bad_param;
    output->value = input->value;
    program_assign_copied = output->value == input->value;
    return (migraphx_status)take_status();
}

EXPORT migraphx_status migraphx_program_create(migraphx_program_t* program)
{
    int status;
    if(program == NULL)
        return migraphx_status_bad_param;
    if(ATOMIC_EXCHANGE(create_null, 0))
    {
        *program = NULL;
    }
    else
    {
        *program = (migraphx_program_t)malloc(sizeof(**program));
        if(*program == NULL)
            return migraphx_status_unknown_error;
        (*program)->value = ATOMIC_INCREMENT(next_value);
        ATOMIC_INCREMENT(program_live_count);
    }
    status = take_status();
    return (migraphx_status)status;
}
