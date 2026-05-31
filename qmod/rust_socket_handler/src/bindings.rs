use std::ffi::c_void;
use std::ptr;
use std::sync::Arc;

use tokio::runtime::{Builder, Runtime};
use tokio::task::JoinHandle;

use crate::socket_handler::{RustSocketServer, SocketTransport};

// pub type PacketBytesCallback = unsafe extern "C" fn(*const u8, usize, *mut c_void);

#[repr(transparent)]
#[derive(Copy, Clone)]
struct UserData(*mut c_void);

impl UserData {
	fn new(ptr: *mut c_void) -> Self {
		Self(ptr)
	}

	fn as_ptr(self) -> *mut c_void {
		self.0
	}
}

// SAFETY: This wrapper only transports an opaque foreign pointer back to the
// caller-provided callback. Rust never dereferences it.
unsafe impl Send for UserData {}
// SAFETY: Sharing this value is safe for the same reason as above; aliasing
// guarantees are the responsibility of the foreign code that owns the pointer.
unsafe impl Sync for UserData {}

/// An opaque handle to a Rust socket server instance, owned by the caller and manipulated through FFI functions.
pub struct RustSocketServerBinding {
	runtime: Runtime,
	server: Arc<RustSocketServer>,
	listen_task: Option<JoinHandle<std::io::Result<()>>>,
}

impl RustSocketServerBinding {
	fn start_listen(&mut self) -> bool {
		if self.listen_task.is_some() {
			return true;
		}

		let server = Arc::clone(&self.server);
		let handle = self.runtime.spawn(async move { server.listen().await });
		self.listen_task = Some(handle);
		true
	}
}

pub extern "C" fn rust_socket_server_init() {
    // init logging
    if let Err(_) = paper2_log::Paper2Logger::init_with_max_level(log::LevelFilter::Debug) {
        eprintln!("Failed to initialize logging");
    }
}

/// Creates a new socket server binding and returns an opaque handle for FFI callers.
///
/// Returns a null pointer on failure.
///
/// # Safety
/// If `callback` is provided, it must point to a valid function for the entire
/// lifetime of the returned server handle. The callback receives packet bytes
/// that are only valid for the duration of the callback invocation.
///
/// `user_data` is passed back to the callback unchanged; if it is dereferenced
/// in foreign code, that dereference must be valid there.
#[unsafe(no_mangle)]
pub extern "C" fn rust_socket_server_new(
	port: u16,
	transport: SocketTransport,
	callback: Option<unsafe extern "C" fn(*const u8, usize, *mut c_void)>,
	user_data: *mut c_void,
) -> *mut RustSocketServerBinding {
	let runtime = match Builder::new_multi_thread().enable_all().build() {
		Ok(rt) => rt,
		Err(_) => return ptr::null_mut(),
	};

	let user_data = UserData::new(user_data);

	let callback_fn = move |packet: &[u8]| {
		if let Some(cb) = callback {
			// Callback contract is controlled by the C++ caller.
			unsafe { cb(packet.as_ptr(), packet.len(), user_data.as_ptr()) };
		}
	};

	let server = match runtime.block_on(RustSocketServer::new_with_transport(
		port,
		transport.into(),
		callback_fn,
	)) {
		Ok(server) => server,
		Err(_) => return ptr::null_mut(),
	};

	let binding = RustSocketServerBinding {
		runtime,
		server: Arc::new(server),
		listen_task: None,
	};

	Box::into_raw(Box::new(binding))
}

/// Frees a server handle previously returned by `rust_socket_server_new`.
///
/// Passing a null pointer is a no-op.
///
/// # Safety
/// `server` must either be null or a pointer returned by
/// `rust_socket_server_new` that has not already been freed.
#[unsafe(no_mangle)]
pub extern "C" fn rust_socket_server_free(server: *mut RustSocketServerBinding) {
	if server.is_null() {
		return;
	}

	// SAFETY: Pointer came from Box::into_raw in rust_socket_server_new.
	unsafe {
		let mut boxed = Box::from_raw(server);
		if let Some(task) = boxed.listen_task.take() {
			task.abort();
		}
	}
}

/// Starts the async listen loop for the server in a background task.
///
/// Returns `false` if `server` is null.
///
/// # Safety
/// `server` must either be null or a valid, uniquely mutable pointer returned
/// by `rust_socket_server_new` that is still owned by the caller.
#[unsafe(no_mangle)]
pub extern "C" fn rust_socket_server_listen(server: *mut RustSocketServerBinding) -> bool {
	if server.is_null() {
		return false;
	}

	// SAFETY: Null is checked above and pointer lifetime is managed by rust_socket_server_free.
	unsafe { (&mut *server).start_listen() }
}

/// Checks whether at least one client is currently connected.
///
/// Returns `false` if `server` is null.
///
/// # Safety
/// `server` must either be null or a valid pointer returned by
/// `rust_socket_server_new` that has not been freed.
#[unsafe(no_mangle)]
pub extern "C" fn rust_socket_server_has_connection(
	server: *const RustSocketServerBinding,
) -> bool {
	if server.is_null() {
		return false;
	}

	// SAFETY: Null is checked above and pointer lifetime is managed by rust_socket_server_free.
	unsafe {
		let binding = &*server;
		binding.runtime.block_on(binding.server.has_connection())
	}
}

/// Sends a framed packet to all currently connected clients.
/// 
/// The packet is framed with a 4-byte big-endian length prefix by the Rust code, so the caller should only provide the raw payload bytes.
///
/// Returns `false` if any pointer is null or if sending fails.
///
/// # Safety
/// `server` must either be null or a valid pointer returned by
/// `rust_socket_server_new` that has not been freed.
///
/// `data` must point to a readable region of memory containing at least `len`
/// bytes for the duration of this call.
#[unsafe(no_mangle)]
pub extern "C" fn rust_socket_server_send_packet(
	server: *const RustSocketServerBinding,
	data: *const u8,
	len: usize,
	blocking: bool,
) -> bool {
	if server.is_null() || data.is_null() {
		return false;
	}

	// SAFETY: Null pointers are checked above; caller provides a valid buffer for len bytes.
	unsafe {
		let binding = &*server;
		let packet = std::slice::from_raw_parts(data, len);
		match blocking {
			true => binding.runtime.block_on(binding.server.send_packet(packet)).is_ok(),
			false => {
				let packet = packet.to_vec();
				let server = Arc::clone(&binding.server);
				binding.runtime.spawn(async move {
					let _ = server.send_packet(&packet).await;
				});
				true
			}
		}
	}
}
