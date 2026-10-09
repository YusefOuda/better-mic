//! Minimal C API over DeepFilterNet3 for micproc.
//!
//! - The DFN3 model is embedded in the library (no model file to ship).
//! - Panics never cross the FFI boundary: every entry point catches them and
//!   reports failure through its return value.
//! - Mono, 48 kHz, frames of `dfs_frame_length()` samples (480 = 10 ms) in [-1, 1].

use std::panic::{catch_unwind, AssertUnwindSafe};

use df::tract::{DfParams, DfTract, RuntimeParams};
use ndarray::{ArrayView2, ArrayViewMut2};

pub struct DfShim {
    model: DfTract,
    noisy: Vec<f32>, // input copy, preallocated to keep the audio path allocation-free
}

/// Creates a denoiser. `atten_lim_db` caps noise attenuation (e.g. 100 = no
/// limit; lower values keep some background for a more natural sound).
/// Returns null on failure.
#[no_mangle]
pub extern "C" fn dfs_create(atten_lim_db: f32) -> *mut DfShim {
    catch_unwind(|| {
        let params = RuntimeParams::default_with_ch(1).with_atten_lim(atten_lim_db);
        DfTract::new(DfParams::default(), &params).ok()
    })
    .ok()
    .flatten()
    .map_or(std::ptr::null_mut(), |model| {
        let noisy = vec![0.0; model.hop_size];
        Box::into_raw(Box::new(DfShim { model, noisy }))
    })
}

/// # Safety
/// `st` must come from `dfs_create` and not be used afterwards. Null is a no-op.
#[no_mangle]
pub unsafe extern "C" fn dfs_destroy(st: *mut DfShim) {
    if !st.is_null() {
        let _ = catch_unwind(AssertUnwindSafe(|| drop(Box::from_raw(st))));
    }
}

/// Samples per frame (hop size), or 0 for a null handle.
///
/// # Safety
/// `st` must be null or a live handle from `dfs_create`.
#[no_mangle]
pub unsafe extern "C" fn dfs_frame_length(st: *const DfShim) -> usize {
    st.as_ref().map_or(0, |s| s.model.hop_size)
}

/// # Safety
/// `st` must be null or a live handle from `dfs_create`.
#[no_mangle]
pub unsafe extern "C" fn dfs_set_atten_lim(st: *mut DfShim, atten_lim_db: f32) -> i32 {
    match st.as_mut() {
        Some(s) => match catch_unwind(AssertUnwindSafe(|| s.model.set_atten_lim(atten_lim_db))) {
            Ok(()) => 0,
            Err(_) => -1,
        },
        None => -1,
    }
}

/// Denoises one frame. `input` and `output` hold `dfs_frame_length()` samples
/// and may point to the same buffer. Returns the frame's estimated local SNR
/// in dB, or NaN on failure (output is then a copy of the input).
///
/// # Safety
/// `st` must be a live handle; buffers must be valid for `dfs_frame_length()` floats.
#[no_mangle]
pub unsafe extern "C" fn dfs_process(st: *mut DfShim, input: *const f32, output: *mut f32) -> f32 {
    let Some(s) = st.as_mut() else { return f32::NAN };
    if input.is_null() || output.is_null() {
        return f32::NAN;
    }
    let n = s.model.hop_size;
    // Copy first so in-place calls are fine and the views never alias.
    s.noisy.copy_from_slice(std::slice::from_raw_parts(input, n));
    let out = std::slice::from_raw_parts_mut(output, n);
    let DfShim { model, noisy } = s;
    let result = catch_unwind(AssertUnwindSafe(|| {
        let noisy_view = ArrayView2::from_shape((1, n), noisy.as_slice()).ok()?;
        let enh_view = ArrayViewMut2::from_shape((1, n), &mut *out).ok()?;
        model.process(noisy_view, enh_view).ok()
    }));
    match result {
        Ok(Some(lsnr)) => lsnr,
        _ => {
            out.copy_from_slice(noisy);
            f32::NAN
        }
    }
}
