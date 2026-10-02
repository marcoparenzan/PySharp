"""Generates src/NDSharp.Tests/OracleCases.g.cs.

Each case is (id, C# expression building an NDArray with NDSharp, Python expression computing the
same thing with real numpy). The generated tests assert that ArrayFormat.Repr(csharp) equals
repr(numpy result) — so both the numeric result, the dtype and the formatting are checked against
the real library. Run with the tools/oracle venv:

    tools/oracle/.venv/Scripts/python.exe tools/oracle/gen_ndsharp_tests.py
"""
import numpy as np, os

# Python-side helpers mirroring the C# helpers in OracleTests.cs
A = lambda *v: np.array(v, dtype=np.float64)
L = lambda *v: np.array(v, dtype=np.int64)
U8 = lambda *v: np.array(v, dtype=np.uint8)
I8 = lambda *v: np.array(v, dtype=np.int8)
F32 = lambda *v: np.array(v, dtype=np.float32)
B = lambda *v: np.array(v, dtype=bool)
M = lambda rows: np.array(rows, dtype=np.float64)
LM = lambda rows: np.array(rows, dtype=np.int64)

cases = [
 # ---------------------------------------------------------------- creation
 ("zeros_2x3", "np.Zeros(new[]{2,3})", "np.zeros((2,3))"),
 ("ones_u8", "np.Ones(new[]{3}, DType.UInt8)", "np.ones(3, dtype=np.uint8)"),
 ("full_int", "np.Full(new[]{2,2}, 7L)", "np.full((2,2), 7)"),
 ("full_float", "np.Full(new[]{2}, 2.5)", "np.full((2,), 2.5)"),
 ("full_bool", "np.Full(new[]{2}, true)", "np.full((2,), True)"),
 ("arange_int", "np.Arange(0L, 5L)", "np.arange(5)"),
 ("arange_step", "np.Arange(1L, 10L, 3L)", "np.arange(1, 10, 3)"),
 ("arange_float", "np.Arange(0.0, 1.0, 0.25)", "np.arange(0, 1, 0.25)"),
 ("arange_neg", "np.Arange(5L, 0L, -2L)", "np.arange(5, 0, -2)"),
 ("linspace", "np.Linspace(0, 10, 5)", "np.linspace(0, 10, 5)"),
 ("linspace_noend", "np.Linspace(0, 1, 4, false)", "np.linspace(0, 1, 4, endpoint=False)"),
 ("eye", "np.Eye(3)", "np.eye(3)"),
 ("eye_k", "np.Eye(3, null, 1)", "np.eye(3, k=1)"),
 ("eye_i32", "np.Eye(2, null, 0, DType.Int32)", "np.eye(2, dtype=np.int32)"),
 ("diag_build", "np.Diag(L(1,2,3))", "np.diag(L(1,2,3))"),
 ("diag_extract", "np.Diag(np.Reshape(np.Arange(0L,9L),3,3))", "np.diag(np.arange(9).reshape(3,3))"),
 ("zeros_like_u8", "np.ZerosLike(U8(1,2))", "np.zeros_like(U8(1,2))"),
 ("full_like", "np.FullLike(A(1,2), 3L)", "np.full_like(A(1,2), 3)"),
 ("meshgrid_x", "np.Meshgrid(new[]{A(1,2,3), A(4,5)})[0]", "np.meshgrid(A(1,2,3), A(4,5))[0]"),
 ("meshgrid_y", "np.Meshgrid(new[]{A(1,2,3), A(4,5)})[1]", "np.meshgrid(A(1,2,3), A(4,5))[1]"),
 ("meshgrid_ij", "np.Meshgrid(new[]{A(1,2,3), A(4,5)}, false)[1]", "np.meshgrid(A(1,2,3), A(4,5), indexing='ij')[1]"),

 # ---------------------------------------------------------------- casting
 ("astype_f2u8", "A(-1.5, 300.7, 255.9, 3.2).AsType(DType.UInt8)", "A(-1.5, 300.7, 255.9, 3.2).astype(np.uint8)"),
 ("astype_f2i8", "A(127.9, 128.0, -129.0).AsType(DType.Int8)", "A(127.9, 128.0, -129.0).astype(np.int8)"),
 ("astype_i2u8", "L(-1, 256, 257, 300).AsType(DType.UInt8)", "L(-1, 256, 257, 300).astype(np.uint8)"),
 ("astype_f2bool", "A(0.0, -0.5, double.NaN).AsType(DType.Bool)", "A(0.0, -0.5, np.nan).astype(bool)"),
 ("astype_bool2f", "B(true,false).AsType(DType.Float32)", "B(True,False).astype(np.float32)"),
 ("astype_f64_f32", "A(0.1, 1.0/3).AsType(DType.Float32)", "A(0.1, 1/3).astype(np.float32)"),
 ("astype_nan_i32", "A(double.NaN, 1e10).AsType(DType.Int32)", "A(np.nan, 1e10).astype(np.int32)"),

 # ---------------------------------------------------------------- arithmetic & promotion
 ("u8_add_weak", "np.Add(U8(250,5,100), W(10L))", "U8(250,5,100) + 10"),
 ("u8_mul_weak", "np.Multiply(U8(250,5,100), W(2L))", "U8(250,5,100) * 2"),
 ("u8_sub_weak", "np.Subtract(U8(250,5,100), W(10L))", "U8(250,5,100) - 10"),
 ("u8_add_u8", "np.Add(U8(250,5,100), U8(10,10,200))", "U8(250,5,100) + U8(10,10,200)"),
 ("u8_add_i8", "np.Add(U8(250,5), I8(-1,-1))", "U8(250,5) + I8(-1,-1)"),
 ("u8_add_i16", "np.Add(U8(250,5), U8(1,1).AsType(DType.Int16))", "U8(250,5) + U8(1,1).astype(np.int16)"),
 ("u8_plus_float_weak", "np.Add(U8(1,2), W(1.5))", "U8(1,2) + 1.5"),
 ("f32_mul_weak_float", "np.Multiply(F32(1,2,3), W(0.1))", "F32(1,2,3) * 0.1"),
 ("i8_plus_float_weak", "np.Add(I8(1,2), W(1.5))", "I8(1,2) + 1.5"),
 ("bool_plus_int_weak", "np.Add(B(true,false), W(1L))", "B(True,False) + 1"),
 ("bool_add_bool", "np.Add(B(true,false), B(true,true))", "B(True,False) + B(True,True)"),
 ("bool_mul_bool", "np.Multiply(B(true,false), B(true,true))", "B(True,False) * B(True,True)"),
 ("i64_div", "np.Divide(L(1,2,3), W(2L))", "L(1,2,3) / 2"),
 ("u8_div_u8", "np.Divide(U8(1,2,3), U8(2,2,2))", "U8(1,2,3) / U8(2,2,2)"),
 ("f32_div", "np.Divide(F32(1,2,3), W(3L))", "F32(1,2,3) / 3"),
 ("floordiv_int", "np.FloorDivide(L(-7,7,-7,7), W(2L))", "L(-7,7,-7,7) // 2"),
 ("floordiv_int_neg", "np.FloorDivide(L(-7,7), W(-2L))", "L(-7,7) // -2"),
 ("mod_int", "np.Mod(L(-7,7,-7,7), W(3L))", "L(-7,7,-7,7) % 3"),
 ("mod_int_negdiv", "np.Mod(L(-7,7), W(-3L))", "L(-7,7) % -3"),
 ("floordiv_float", "np.FloorDivide(A(-7.5,7.5,-0.5), W(2L))", "A(-7.5,7.5,-0.5) // 2"),
 ("mod_float", "np.Mod(A(-7.5,7.5,5.5), W(2L))", "A(-7.5,7.5,5.5) % 2"),
 ("pow_int", "np.Power(L(1,2,3), W(2L))", "L(1,2,3) ** 2"),
 ("pow_int_rscalar", "np.Power(W(2L), L(1,2,3))", "2 ** L(1,2,3)"),
 ("pow_float", "np.Power(A(1,2,3), W(0.5))", "A(1,2,3) ** 0.5"),
 ("pow_u8_wrap", "np.Power(U8(2,3,16), W(4L))", "U8(2,3,16) ** 4"),
 ("neg_i64", "np.Negative(L(1,-2,3))", "-L(1,-2,3)"),
 ("neg_u8_wrap", "np.Negative(U8(1,2))", "-U8(1,2)"),
 ("abs_i8", "np.Abs(I8(-3,4,-128))", "np.abs(I8(-3,4,-128))"),
 ("sign_f", "np.Sign(A(-2.5,0,3))", "np.sign(A(-2.5,0,3))"),
 ("square_u8", "np.Square(U8(10,20))", "np.square(U8(10,20))"),

 # ---------------------------------------------------------------- float functions
 ("sqrt_f64", "np.Sqrt(A(1,4,9))", "np.sqrt(A(1,4,9))"),
 ("sqrt_u8_f16", "np.Sqrt(U8(1,4,9))", "np.sqrt(U8(1,4,9))"),
 ("sqrt_i16_f32", "np.Sqrt(I8(1,4,9).AsType(DType.Int16))", "np.sqrt(I8(1,4,9).astype(np.int16))"),
 ("sqrt_i64", "np.Sqrt(L(1,4,9))", "np.sqrt(L(1,4,9))"),
 ("log_f64", "np.Log(A(1, Math.E, 10))", "np.log(A(1, np.e, 10))"),
 ("sin_cos", "np.Add(np.Sin(A(0,1)), np.Cos(A(0,1)))", "np.sin(A(0,1)) + np.cos(A(0,1))"),
 ("arctan2", "np.Arctan2(A(1,-1), A(-1,-1))", "np.arctan2(A(1,-1), A(-1,-1))"),
 ("hypot", "np.Hypot(A(3,5), A(4,12))", "np.hypot(A(3,5), A(4,12))"),
 ("degrees", "np.Degrees(A(0, Math.PI, Math.PI/2))", "np.degrees(A(0, np.pi, np.pi/2))"),
 ("radians", "np.Radians(A(0,90,180))", "np.radians(A(0,90,180))"),
 ("round_half_even", "np.Round(A(0.5,1.5,2.5,-0.5,-1.5))", "np.round(A(0.5,1.5,2.5,-0.5,-1.5))"),
 ("round_dec", "np.Round(A(1.234,5.678,-0.001), 1)", "np.round(A(1.234,5.678,-0.001), 1)"),
 ("round_int", "np.Round(L(1,2))", "np.round(L(1,2))"),
 ("floor_f", "np.Floor(A(1.5,-1.5))", "np.floor(A(1.5,-1.5))"),
 ("ceil_int_keep", "np.Ceil(L(1,2))", "np.ceil(L(1,2))"),
 ("isnan", "np.IsNan(A(1, double.NaN, double.PositiveInfinity))", "np.isnan(A(1, np.nan, np.inf))"),
 ("isfinite", "np.IsFinite(A(1, double.NaN, double.PositiveInfinity))", "np.isfinite(A(1, np.nan, np.inf))"),

 # ---------------------------------------------------------------- comparison / logic
 ("gt_weak", "np.Greater(L(1,2,3), W(2L))", "L(1,2,3) > 2"),
 ("eq_arrays", "np.Equal(L(1,2,3), L(1,5,3))", "L(1,2,3) == L(1,5,3)"),
 ("lt_u8_big", "np.Less(U8(1,255), W(300L))", "U8(1,255) < 300"),
 ("eq_nan", "np.Equal(A(double.NaN,1), A(double.NaN,1))", "A(np.nan,1) == A(np.nan,1)"),
 ("invert_bool", "np.Invert(B(true,false))", "~B(True,False)"),
 ("invert_u8", "np.Invert(U8(0,1,255))", "~U8(0,1,255)"),
 ("invert_i64", "np.Invert(L(0,1,-1))", "~L(0,1,-1)"),
 ("and_i64", "np.BitwiseAnd(L(12,10), W(6L))", "L(12,10) & 6"),
 ("or_u8", "np.BitwiseOr(U8(12,10), W(1L))", "U8(12,10) | 1"),
 ("xor_bool", "np.BitwiseXor(B(true,false,true), B(true,true,false))", "B(True,False,True) ^ B(True,True,False)"),
 ("shl", "np.LeftShift(L(1,2), W(3L))", "L(1,2) << 3"),
 ("shr_neg", "np.RightShift(L(-8,8), W(1L))", "L(-8,8) >> 1"),
 ("logical_and", "np.LogicalAnd(L(0,1,2), L(1,1,0))", "np.logical_and(L(0,1,2), L(1,1,0))"),
 ("logical_not", "np.LogicalNot(L(0,1,2))", "np.logical_not(L(0,1,2))"),
 ("where_scalar", "np.Where(np.Greater(L(1,2,3), W(2L)), L(1,2,3), W(0L))", "np.where(L(1,2,3) > 2, L(1,2,3), 0)"),
 ("where_f", "np.Where(B(true,false), A(1,2), A(10,20))", "np.where(B(True,False), A(1,2), A(10,20))"),
 ("where_u8_weak", "np.Where(B(true,false), U8(1,2), W(200L))", "np.where(B(True,False), U8(1,2), 200)"),
 ("clip_i64", "np.Clip(L(-5,0,300,100), W(0L), W(255L))", "np.clip(L(-5,0,300,100), 0, 255)"),
 ("clip_u8", "np.Clip(U8(0,100,250), W(10L), W(200L))", "np.clip(U8(0,100,250), 10, 200)"),
 ("clip_f_into", "np.Clip(A(-1.5,0.5,9.0), W(0.0), W(1.0))", "np.clip(A(-1.5,0.5,9.0), 0.0, 1.0)"),
 ("maximum_nan", "np.Maximum(A(1,double.NaN,3), A(2,2,double.NaN))", "np.maximum(A(1,np.nan,3), A(2,2,np.nan))"),
 ("minimum", "np.Minimum(L(1,5), L(3,2))", "np.minimum(L(1,5), L(3,2))"),

 # ---------------------------------------------------------------- broadcasting
 ("bcast_col_row", "np.Add(np.Reshape(np.Arange(0L,3L),3,1), np.Arange(0L,4L))", "np.arange(3).reshape(3,1) + np.arange(4)"),
 ("bcast_mul", "np.Multiply(LM(new long[,]{{1},{2}}), L(10,20,30))", "LM([[1],[2]]) * L(10,20,30)"),
 ("bcast_to", "np.BroadcastTo(L(1,2,3), new[]{2,3})", "np.broadcast_to(L(1,2,3), (2,3))"),
 ("bcast_scalar_arr", "np.Subtract(W(10L), L(1,2,3))", "10 - L(1,2,3)"),

 # ---------------------------------------------------------------- reductions
 ("sum_i64", "np.Sum(L(1,2,3))", "np.sum(L(1,2,3))"),
 ("sum_u8_widens", "np.Sum(U8(200,100))", "np.sum(U8(200,100))"),
 ("sum_f32", "np.Sum(F32(0.1f,0.2f,0.3f))", "np.sum(F32(0.1,0.2,0.3))"),
 ("sum_bool", "np.Sum(B(true,true,false))", "np.sum(B(True,True,False))"),
 ("sum_axis0", "np.Sum(np.Reshape(np.Arange(0L,6L),2,3), new[]{0})", "np.arange(6).reshape(2,3).sum(axis=0)"),
 ("sum_axis1", "np.Sum(np.Reshape(np.Arange(0L,6L),2,3), new[]{1})", "np.arange(6).reshape(2,3).sum(axis=1)"),
 ("sum_axis_neg", "np.Sum(np.Reshape(np.Arange(0L,6L),2,3), new[]{-1})", "np.arange(6).reshape(2,3).sum(axis=-1)"),
 ("sum_keepdims", "np.Sum(np.Reshape(np.Arange(0L,6L),2,3), new[]{1}, true)", "np.arange(6).reshape(2,3).sum(axis=1, keepdims=True)"),
 ("sum_axes_tuple", "np.Sum(np.Reshape(np.Arange(0L,24L),2,3,4), new[]{0,2})", "np.arange(24).reshape(2,3,4).sum(axis=(0,2))"),
 ("sum_T", "np.Sum(np.Transpose(np.Reshape(np.Arange(0L,6L),2,3)), new[]{0})", "np.arange(6).reshape(2,3).T.sum(axis=0)"),
 ("prod_i64", "np.Prod(L(1,2,3,4))", "np.prod(L(1,2,3,4))"),
 ("prod_empty", "np.Prod(np.Zeros(new[]{0}))", "np.prod(np.zeros(0))"),
 ("sum_empty", "np.Sum(np.Zeros(new[]{0}))", "np.sum(np.zeros(0))"),
 ("mean_int", "np.Mean(L(1,2,3,4))", "np.mean(L(1,2,3,4))"),
 ("mean_f32", "np.Mean(F32(0.1f,0.2f,0.3f))", "np.mean(F32(0.1,0.2,0.3))"),
 ("mean_axis", "np.Mean(np.Reshape(np.Arange(0L,6L),2,3), new[]{0})", "np.arange(6).reshape(2,3).mean(axis=0)"),
 ("mean_u8", "np.Mean(U8(200,100,255))", "np.mean(U8(200,100,255))"),
 ("std", "np.Std(A(1,2,3,4))", "np.std(A(1,2,3,4))"),
 ("var_ddof", "np.Var(A(1,2,3,4), null, false, 1)", "np.var(A(1,2,3,4), ddof=1)"),
 ("var_axis", "np.Var(np.Reshape(np.Arange(0L,6L),2,3), new[]{1})", "np.arange(6).reshape(2,3).var(axis=1)"),
 ("std_int_axis0", "np.Std(np.Reshape(np.Arange(0L,6L),2,3), new[]{0})", "np.arange(6).reshape(2,3).std(axis=0)"),
 ("max_i8", "np.Max(I8(-3,4,2))", "np.max(I8(-3,4,2))"),
 ("min_axis", "np.Min(np.Reshape(np.Arange(5L,11L),2,3), new[]{1})", "np.arange(5,11).reshape(2,3).min(axis=1)"),
 ("max_nan", "np.Max(A(1,double.NaN,3))", "np.max(A(1,np.nan,3))"),
 ("max_bool", "np.Max(B(true,false))", "np.max(B(True,False))"),
 ("ptp", "np.Ptp(A(1,5,3))", "np.ptp(A(1,5,3))"),
 ("argmax_flat", "np.ArgMax(LM(new long[,]{{1,9},{3,2}}))", "np.argmax(LM([[1,9],[3,2]]))"),
 ("argmax_axis0", "np.ArgMax(LM(new long[,]{{1,9},{3,2}}), 0)", "np.argmax(LM([[1,9],[3,2]]), axis=0)"),
 ("argmin_axis1", "np.ArgMin(LM(new long[,]{{1,9},{3,2}}), 1)", "np.argmin(LM([[1,9],[3,2]]), axis=1)"),
 ("argmax_nan", "np.ArgMax(A(1,double.NaN,3))", "np.argmax(A(1,np.nan,3))"),
 ("argmax_ties", "np.ArgMax(L(3,3,1))", "np.argmax(L(3,3,1))"),
 ("cumsum_flat", "np.CumSum(LM(new long[,]{{1,2},{3,4}}))", "np.cumsum(LM([[1,2],[3,4]]))"),
 ("cumsum_axis", "np.CumSum(LM(new long[,]{{1,2},{3,4}}), 0)", "np.cumsum(LM([[1,2],[3,4]]), axis=0)"),
 ("cumsum_u8", "np.CumSum(U8(200,100))", "np.cumsum(U8(200,100))"),
 ("cumprod", "np.CumProd(L(1,2,3,4))", "np.cumprod(L(1,2,3,4))"),
 ("any_axis", "np.Any(LM(new long[,]{{0,0},{0,3}}), new[]{1})", "np.any(LM([[0,0],[0,3]]), axis=1)"),
 ("all_axis", "np.All(LM(new long[,]{{1,2},{0,3}}), new[]{0})", "np.all(LM([[1,2],[0,3]]), axis=0)"),
 ("count_nonzero", "np.CountNonzero(L(0,1,2,0))", "np.count_nonzero(L(0,1,2,0))"),
 ("norm", "np.Norm(A(3,4))", "np.linalg.norm(A(3,4))"),
 ("norm_axis", "np.Norm(M_(new double[,]{{3,4},{6,8}}), new[]{1})", "np.linalg.norm(M([[3,4],[6,8]]), axis=1)"),

 # ---------------------------------------------------------------- shape
 ("reshape_neg1", "np.Reshape(np.Arange(0L,6L), 3, -1)", "np.arange(6).reshape(3,-1)"),
 ("transpose_2d", "np.Transpose(np.Reshape(np.Arange(0L,6L),2,3))", "np.arange(6).reshape(2,3).T"),
 ("transpose_axes", "np.Transpose(np.Reshape(np.Arange(0L,24L),2,3,4), new[]{1,0,2})", "np.arange(24).reshape(2,3,4).transpose(1,0,2)"),
 ("swapaxes", "np.SwapAxes(np.Reshape(np.Arange(0L,24L),2,3,4), 0, 2)", "np.arange(24).reshape(2,3,4).swapaxes(0,2)"),
 ("moveaxis", "np.MoveAxis(np.Reshape(np.Arange(0L,24L),2,3,4), 0, -1)", "np.moveaxis(np.arange(24).reshape(2,3,4), 0, -1)"),
 ("ravel_T", "np.Ravel(np.Transpose(np.Reshape(np.Arange(0L,6L),2,3)))", "np.arange(6).reshape(2,3).T.ravel()"),
 ("flatten", "np.Flatten(np.Reshape(np.Arange(0L,6L),2,3))", "np.arange(6).reshape(2,3).flatten()"),
 ("concat_axis1", "np.Concatenate(new[]{np.Reshape(np.Arange(0L,4L),2,2), np.Reshape(np.Arange(10L,14L),2,2)}, 1)", "np.concatenate([np.arange(4).reshape(2,2), np.arange(10,14).reshape(2,2)], axis=1)"),
 ("concat_promote", "np.Concatenate(new[]{L(1,2), A(0.5)})", "np.concatenate([L(1,2), A(0.5)])"),
 ("stack_axis1", "np.Stack(new[]{L(1,2,3), L(4,5,6)}, 1)", "np.stack([L(1,2,3), L(4,5,6)], axis=1)"),
 ("vstack_1d", "np.VStack(new[]{L(1,2), L(3,4)})", "np.vstack([L(1,2), L(3,4)])"),
 ("hstack_1d", "np.HStack(new[]{L(1,2), L(3,4)})", "np.hstack([L(1,2), L(3,4)])"),
 ("hstack_2d", "np.HStack(new[]{np.Reshape(L(1,2),2,1), np.Reshape(L(3,4),2,1)})", "np.hstack([L(1,2).reshape(2,1), L(3,4).reshape(2,1)])"),
 ("flip", "np.Flip(np.Reshape(np.Arange(0L,6L),2,3), 1)", "np.flip(np.arange(6).reshape(2,3), axis=1)"),
 ("fliplr", "np.Fliplr(np.Reshape(np.Arange(0L,6L),2,3))", "np.fliplr(np.arange(6).reshape(2,3))"),
 ("flipud", "np.Flipud(np.Reshape(np.Arange(0L,6L),2,3))", "np.flipud(np.arange(6).reshape(2,3))"),
 ("roll_1d", "np.Roll(np.Arange(0L,5L), 2, 0)", "np.roll(np.arange(5), 2)"),
 ("roll_neg", "np.Roll(np.Arange(0L,5L), -1, 0)", "np.roll(np.arange(5), -1)"),
 ("roll_axis1", "np.Roll(np.Reshape(np.Arange(0L,6L),2,3), 1, 1)", "np.roll(np.arange(6).reshape(2,3), 1, axis=1)"),
 ("roll_flat", "np.Roll(np.Reshape(np.Arange(0L,6L),2,3), 1)", "np.roll(np.arange(6).reshape(2,3), 1)"),
 ("tile", "np.Tile(L(1,2), 2, 2)", "np.tile(L(1,2), (2,2))"),
 ("repeat", "np.Repeat(L(1,2), 3)", "np.repeat(L(1,2), 3)"),
 ("repeat_axis", "np.Repeat(np.Reshape(L(1,2,3,4),2,2), 2, 0)", "np.repeat(L(1,2,3,4).reshape(2,2), 2, axis=0)"),
 ("expand_dims", "np.ExpandDims(L(1,2), 0)", "np.expand_dims(L(1,2), 0)"),
 ("squeeze", "np.Squeeze(np.Reshape(L(1,2),1,2,1))", "np.squeeze(L(1,2).reshape(1,2,1))"),
 ("pad_const", "np.PadConstant(np.Reshape(L(1,2,3,4),2,2), new[]{(1,1)})", "np.pad(L(1,2,3,4).reshape(2,2), 1)"),
 ("pad_asym", "np.PadConstant(L(1,2,3), new[]{(2,1)})", "np.pad(L(1,2,3), (2,1))"),

 # ---------------------------------------------------------------- indexing (get)
 ("idx_slice", "L(0,1,2,3,4).Get(new Slice(1,3))", "L(0,1,2,3,4)[1:3]"),
 ("idx_rev", "L(0,1,2,3,4).Get(new Slice(null,null,-1))", "L(0,1,2,3,4)[::-1]"),
 ("idx_step", "L(0,1,2,3,4).Get(new Slice(null,null,2))", "L(0,1,2,3,4)[::2]"),
 ("idx_neg_start", "L(0,1,2,3,4).Get(new Slice(-2,null))", "L(0,1,2,3,4)[-2:]"),
 ("idx_col", "M6().Get(Slice.All, 1)", "np.arange(6).reshape(2,3)[:, 1]"),
 ("idx_row", "M6().Get(1, Slice.All)", "np.arange(6).reshape(2,3)[1, :]"),
 ("idx_int", "M6().Get(1)", "np.arange(6).reshape(2,3)[1]"),
 ("idx_ellipsis", "M6().Get(NDIndex.Ellipsis, 0)", "np.arange(6).reshape(2,3)[..., 0]"),
 ("idx_newaxis", "L(1,2,3).Get(NDIndex.NewAxis, Slice.All)", "L(1,2,3)[None, :]"),
 ("idx_newaxis_end", "L(1,2,3).Get(Slice.All, NDIndex.NewAxis)", "L(1,2,3)[:, None]"),
 ("idx_2d_slice", "M6().Get(new Slice(0,2), new Slice(1,3))", "np.arange(6).reshape(2,3)[0:2, 1:3]"),
 ("idx_T_slice", "np.Transpose(M6()).Get(new Slice(null,null,-1), 0)", "np.arange(6).reshape(2,3).T[::-1, 0]"),
 ("fancy_1d", "L(10,20,30,40).Get(L(0,2,2))", "L(10,20,30,40)[[0,2,2]]"),
 ("fancy_neg", "L(10,20,30,40).Get(L(-1,0))", "L(10,20,30,40)[[-1,0]]"),
 ("fancy_2d_rows", "M6().Get(L(1,0))", "np.arange(6).reshape(2,3)[[1,0]]"),
 ("fancy_pair", "M6().Get(L(0,1), L(2,0))", "np.arange(6).reshape(2,3)[[0,1],[2,0]]"),
 ("fancy_cols", "M6().Get(Slice.All, L(0,2))", "np.arange(6).reshape(2,3)[:, [0,2]]"),
 ("fancy_rows_slice", "M6().Get(L(1,0), new Slice(1,null))", "np.arange(6).reshape(2,3)[[1,0], 1:]"),
 ("fancy_int_and_arr", "M6().Get(0, L(2,0))", "np.arange(6).reshape(2,3)[0, [2,0]]"),
 ("fancy_nonadjacent", "np.Reshape(np.Arange(0L,24L),2,3,4).Get(L(0,1), Slice.All, L(1,2))", "np.arange(24).reshape(2,3,4)[[0,1], :, [1,2]]"),
 ("fancy_2d_index", "L(10,20,30,40).Get(LM(new long[,]{{0,1},{2,3}}))", "L(10,20,30,40)[LM([[0,1],[2,3]])]"),
 ("mask_1d", "L(1,2,3,4).Get(B(true,false,true,false))", "L(1,2,3,4)[B(True,False,True,False)]"),
 ("mask_from_cmp", "M6().Get(np.Greater(M6(), W(2L)))", "np.arange(6).reshape(2,3)[np.arange(6).reshape(2,3) > 2]"),
 ("mask_rows", "M6().Get(B(false,true))", "np.arange(6).reshape(2,3)[B(False,True)]"),
 ("mask_and_slice", "M6().Get(B(true,false), Slice.All)", "np.arange(6).reshape(2,3)[B(True,False), :]"),

 # ---------------------------------------------------------------- matmul / products
 ("matmul_22", "np.MatMul(M_(new double[,]{{1,2},{3,4}}), M_(new double[,]{{5,6},{7,8}}))", "M([[1,2],[3,4]]) @ M([[5,6],[7,8]])"),
 ("matmul_23_32", "np.MatMul(M6(), np.Transpose(M6()))", "np.arange(6).reshape(2,3) @ np.arange(6).reshape(2,3).T"),
 ("matmul_1d_1d", "np.MatMul(L(1,2,3), L(4,5,6))", "L(1,2,3) @ L(4,5,6)"),
 ("matmul_2d_1d", "np.MatMul(M6(), L(1,1,1))", "np.arange(6).reshape(2,3) @ L(1,1,1)"),
 ("matmul_1d_2d", "np.MatMul(L(1,1), M6())", "L(1,1) @ np.arange(6).reshape(2,3)"),
 ("matmul_batch", "np.MatMul(np.Reshape(np.Arange(0L,8L),2,2,2), np.Reshape(np.Arange(0L,4L),2,2))", "np.arange(8).reshape(2,2,2) @ np.arange(4).reshape(2,2)"),
 ("matmul_f32", "np.MatMul(F32(1,2).AsType(DType.Float32).Get(NDIndex.NewAxis, Slice.All), F32(3,4).Get(Slice.All, NDIndex.NewAxis))", "F32(1,2)[None,:] @ F32(3,4)[:,None]"),
 ("matmul_u8", "np.MatMul(U8(200,2), U8(2,3))", "U8(200,2) @ U8(2,3)"),
 ("dot_scalar", "np.Dot(W(2L), L(1,2))", "np.dot(2, L(1,2))"),
 ("outer", "np.Outer(L(1,2,3), L(10,20))", "np.outer(L(1,2,3), L(10,20))"),
 ("trace", "np.Trace(M6().Get(Slice.All, new Slice(0,2)))", "np.trace(np.arange(6).reshape(2,3)[:, 0:2])"),
]

# set-item cases: (id, setup C#, statement C#, python setup, python stmt, final var)
set_cases = [
 ("set_slice", "var x = L(0,1,2,3,4);", "x.Put(W(9L), new Slice(1,3));", "x = L(0,1,2,3,4)", "x[1:3] = 9"),
 ("set_slice_arr", "var x = L(0,1,2,3,4);", "x.Put(L(7,8), new Slice(1,3));", "x = L(0,1,2,3,4)", "x[1:3] = L(7,8)"),
 ("set_bcast_row", "var x = np.Zeros(new[]{2,3}, DType.Int64);", "x.Put(L(1,2,3), Slice.All);", "x = np.zeros((2,3), dtype=np.int64)", "x[:] = L(1,2,3)"),
 ("set_float_into_int", "var x = L(0,0,0);", "x.Put(W(2.7), Slice.All);", "x = L(0,0,0)", "x[:] = 2.7"),
 ("set_u8_wrap", "var x = U8(0,0);", "x.Put(W(300L), Slice.All);", "x = U8(0,0)", "x[:] = 300 % 256"),
 ("set_mask", "var x = L(1,2,3,4);", "x.Put(W(0L), B(true,false,true,false));", "x = L(1,2,3,4)", "x[B(True,False,True,False)] = 0"),
 ("set_mask_cmp", "var x = M6();", "x.Put(W(-1L), np.Greater(x, W(2L)));", "x = np.arange(6).reshape(2,3)", "x[x > 2] = -1"),
 ("set_mask_arr", "var x = L(1,2,3,4);", "x.Put(L(10,30), B(true,false,true,false));", "x = L(1,2,3,4)", "x[B(True,False,True,False)] = L(10,30)"),
 ("set_fancy", "var x = L(0,0,0,0);", "x.Put(L(5,6), L(0,2));", "x = L(0,0,0,0)", "x[[0,2]] = L(5,6)"),
 ("set_fancy_2d", "var x = np.Zeros(new[]{2,3}, DType.Int64);", "x.Put(W(1L), L(0,1), L(2,0));", "x = np.zeros((2,3), dtype=np.int64)", "x[[0,1],[2,0]] = 1"),
 ("set_view_shares", "var x = M6(); var v = x.Get(1, Slice.All);", "v.Put(W(0L), Slice.All);", "x = np.arange(6).reshape(2,3); v = x[1, :]", "v[:] = 0"),
 ("set_T_view", "var x = M6();", "np.Transpose(x).Put(W(0L), 0, Slice.All);", "x = np.arange(6).reshape(2,3)", "x.T[0, :] = 0"),
 ("set_overlap", "var x = L(0,1,2,3,4);", "x.Put(x.Get(new Slice(0,4)), new Slice(1,5));", "x = L(0,1,2,3,4)", "x[1:5] = x[0:4]"),
]

here = os.path.dirname(os.path.abspath(__file__))
out_path = os.path.normpath(os.path.join(here, "..", "..", "src", "NDSharp.Tests", "OracleCases.g.cs"))

def py_eval(expr, env, extra=None):
    ns = dict(np=np, A=A, L=L, U8=U8, I8=I8, F32=F32, B=B, M=M, LM=LM)
    ns.update(env or {})
    return eval(expr, ns)

def lit(s):
    return '"' + s.replace('\\', '\\\\').replace('"', '\\"').replace('\n', '\\n') + '"'

lines = ["// <auto-generated> by tools/oracle/gen_ndsharp_tests.py — do not edit. Expected values are numpy %s repr()s. </auto-generated>" % np.__version__,
         "// Copyright (c) 2026 Marco Parenzan", "// Licensed under the MIT License. See the LICENSE file in the project root for full license information.", "",
         "using NDSharp;", "", "namespace NDSharp.Tests;", "", "public partial class OracleTests", "{"]
seen = set()
for cid, cs, py in cases:
    assert cid not in seen, cid
    seen.add(cid)
    r = py_eval(py, {})
    if isinstance(r, np.generic): r = np.asarray(r)  # numpy scalars compare as 0-d arrays
    expected = repr(r)
    lines.append(f"    [Fact] public void {cid}() => Check({lit(expected)}, {cs});")
for cid, cs_setup, cs_stmt, py_setup, py_stmt in set_cases:
    ns = dict(np=np, A=A, L=L, U8=U8, I8=I8, F32=F32, B=B, M=M, LM=LM)
    exec(py_setup + "\n" + py_stmt, ns)
    expected = repr(ns["x"])
    lines.append(f"    [Fact] public void {cid}() {{ {cs_setup} {cs_stmt} Check({lit(expected)}, x); }}")
lines.append("}")
open(out_path, "w", encoding="utf-8").write("\n".join(lines) + "\n")
print(len(cases) + len(set_cases), "cases ->", out_path)
