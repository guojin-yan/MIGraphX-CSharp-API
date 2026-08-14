#include <stdio.h>
#include <migraphx.h>

int main(void)
{
    printf("status_size=%zu\n", sizeof(migraphx_status));
    printf("shape_datatype_size=%zu\n", sizeof(migraphx_shape_datatype_t));
    printf("size_t_size=%zu\n", sizeof(size_t));
    printf("bool_size=%zu\n", sizeof(bool));
    printf("opaque_handle_size=%zu\n", sizeof(migraphx_target_t));
    printf("callback_pointer_size=%zu\n", sizeof(migraphx_experimental_custom_op_compute));
    printf("status_success=%d\n", (int)migraphx_status_success);
    printf("status_bad_param=%d\n", (int)migraphx_status_bad_param);
    printf("status_unknown_target=%d\n", (int)migraphx_status_unknown_target);
    printf("status_unknown_error=%d\n", (int)migraphx_status_unknown_error);
    printf("shape_tuple=%d\n", (int)migraphx_shape_tuple_type);
    printf("shape_fp8e5m2fnuz=%d\n", (int)migraphx_shape_fp8e5m2fnuz_type);
    return 0;
}
