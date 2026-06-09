#pragma once

#include <stdarg.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdlib.h>

#ifdef __cplusplus
namespace LiveStreamQuestRust {
namespace ffi {
#endif  // __cplusplus

typedef enum SocketTransport {
  TCP = 0,
  UDP = 1,
} SocketTransport;

/**
 * An opaque handle to a Rust socket server instance, owned by the caller and manipulated through FFI functions.
 */
typedef struct RustSocketServerBinding RustSocketServerBinding;

#ifdef __cplusplus
extern "C" {
#endif // __cplusplus

void rust_socket_server_init(void);

/**
 * Creates a new socket server binding and returns an opaque handle for FFI callers.
 *
 * Returns a null pointer on failure.
 *
 * # Safety
 * If `callback` is provided, it must point to a valid function for the entire
 * lifetime of the returned server handle. The callback receives packet bytes
 * that are only valid for the duration of the callback invocation.
 *
 * `user_data` is passed back to the callback unchanged; if it is dereferenced
 * in foreign code, that dereference must be valid there.
 */
struct RustSocketServerBinding *rust_socket_server_new(uint16_t port,
                                                       enum SocketTransport transport,
                                                       void (*callback)(const uint8_t*,
                                                                        uintptr_t,
                                                                        void*),
                                                       void *user_data);

/**
 * Frees a server handle previously returned by `rust_socket_server_new`.
 *
 * Passing a null pointer is a no-op.
 *
 * # Safety
 * `server` must either be null or a pointer returned by
 * `rust_socket_server_new` that has not already been freed.
 */
void rust_socket_server_free(struct RustSocketServerBinding *server);

/**
 * Starts the async listen loop for the server in a background task.
 *
 * Returns `false` if `server` is null.
 *
 * # Safety
 * `server` must either be null or a valid, uniquely mutable pointer returned
 * by `rust_socket_server_new` that is still owned by the caller.
 */
bool rust_socket_server_listen(struct RustSocketServerBinding *server);

/**
 * Checks whether at least one client is currently connected.
 *
 * Returns `false` if `server` is null.
 *
 * # Safety
 * `server` must either be null or a valid pointer returned by
 * `rust_socket_server_new` that has not been freed.
 */
bool rust_socket_server_has_connection(const struct RustSocketServerBinding *server);

/**
 * Sends a framed packet to all currently connected clients.
 * 
 * The packet is framed with a 4-byte big-endian length prefix by the Rust code, so the caller should only provide the raw payload bytes.
 *
 * Returns `false` if any pointer is null or if sending fails.
 *
 * # Safety
 * `server` must either be null or a valid pointer returned by
 * `rust_socket_server_new` that has not been freed.
 *
 * `data` must point to a readable region of memory containing at least `len`
 * bytes for the duration of this call.
 */
bool rust_socket_server_send_packet(const struct RustSocketServerBinding *server,
                                    const uint8_t *data,
                                    uintptr_t len,
                                    bool blocking);

#ifdef __cplusplus
}  // extern "C"
#endif  // __cplusplus

#ifdef __cplusplus
}  // namespace ffi
}  // namespace LiveStreamQuestRust
#endif  // __cplusplus
