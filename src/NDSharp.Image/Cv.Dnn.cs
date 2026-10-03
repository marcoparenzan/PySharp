// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Runtime.InteropServices;
using OpenCvSharp;
using OpenCvSharp.Dnn;

namespace NDSharp.Image;

public static partial class Cv
{
    /// <summary>cv2.FaceDetectorYN (the YuNet ONNX face detector). OpenCvSharp has no wrapper for it, so this runs the network through
    /// OpenCV's DNN module and reproduces OpenCV's own pre/post-processing: bottom/right padding to a multiple of 32, the three stride
    /// levels (8, 16, 32), score = sqrt(cls * obj), box/landmark decoding and NMS.</summary>
    public sealed class FaceDetectorYuNet : IDisposable
    {
        private static readonly string[] OutputNames =
        {
            "cls_8", "cls_16", "cls_32", "obj_8", "obj_16", "obj_32", "bbox_8", "bbox_16", "bbox_32", "kps_8", "kps_16", "kps_32",
        };
        private static readonly int[] Strides = { 8, 16, 32 };
        private const int Divisor = 32;

        private readonly Net _net;
        public int InputWidth { get; private set; }
        public int InputHeight { get; private set; }
        public float ScoreThreshold { get; set; }
        public float NmsThreshold { get; set; }
        public int TopK { get; set; }

        public FaceDetectorYuNet(string modelPath, int inputWidth, int inputHeight, float scoreThreshold = 0.9f, float nmsThreshold = 0.3f, int topK = 5000)
        {
            if (!File.Exists(modelPath)) throw new NDValueException($"cannot open the ONNX model '{modelPath}'");
            _net = CvDnn.ReadNetFromOnnx(modelPath);
            InputWidth = inputWidth; InputHeight = inputHeight;
            ScoreThreshold = scoreThreshold; NmsThreshold = nmsThreshold; TopK = topK;
        }

        public void SetInputSize(int width, int height) { InputWidth = width; InputHeight = height; }

        /// <summary>Faces as an N×15 float32 array (x, y, w, h, 5 landmark points, score), or null when none is found.</summary>
        public NDArray? Detect(NDArray image)
        {
            using var src = Mats.ToMat(image);
            if (src.Cols != InputWidth || src.Rows != InputHeight)
                throw new NDValueException($"the input image size ({src.Cols}x{src.Rows}) must equal the detector input size ({InputWidth}x{InputHeight}); call setInputSize first");
            int padH = (InputHeight - 1) / Divisor * Divisor + Divisor, padW = (InputWidth - 1) / Divisor * Divisor + Divisor;
            using var padded = new Mat();
            Cv2.CopyMakeBorder(src, padded, 0, padH - InputHeight, 0, padW - InputWidth, BorderTypes.Constant, Scalar.All(0));
            using var blob = CvDnn.BlobFromImage(padded, 1.0, default, default, false, false); // C++ defaults (OpenCvSharp defaults to swapRB/crop = true)
            _net.SetInput(blob);
            var outs = OutputNames.Select(_ => new Mat()).ToArray();
            try
            {
                _net.Forward(outs, OutputNames);
                var data = outs.Select(Floats).ToArray();
                return PostProcess(data, padW, padH);
            }
            finally { foreach (var o in outs) o.Dispose(); }
        }

        private static float[] Floats(Mat m)
        {
            long n = m.Total() * m.Channels();
            var arr = new float[n];
            Marshal.Copy(m.Data, arr, 0, (int)n);
            return arr;
        }

        private NDArray? PostProcess(float[][] o, int padW, int padH)
        {
            var faces = new List<float[]>();
            for (int i = 0; i < Strides.Length; i++)
            {
                int stride = Strides[i], cols = padW / stride, rows = padH / stride;
                float[] cls = o[i], obj = o[i + 3], bbox = o[i + 6], kps = o[i + 9];
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < cols; c++)
                    {
                        int idx = r * cols + c;
                        float clsScore = Math.Max(Math.Min(cls[idx], 1f), 0f), objScore = Math.Max(Math.Min(obj[idx], 1f), 0f);
                        float score = MathF.Sqrt(clsScore * objScore);
                        float cx = (c + bbox[idx * 4 + 0]) * stride, cy = (r + bbox[idx * 4 + 1]) * stride;
                        float w = MathF.Exp(bbox[idx * 4 + 2]) * stride, h = MathF.Exp(bbox[idx * 4 + 3]) * stride;
                        float x1 = cx - w / 2f, y1 = cy - h / 2f;
                        var face = new float[15];
                        face[0] = x1; face[1] = y1; face[2] = w; face[3] = h;
                        for (int n = 0; n < 5; n++)
                        {
                            face[4 + 2 * n] = (kps[idx * 10 + 2 * n] + c) * stride;
                            face[4 + 2 * n + 1] = (kps[idx * 10 + 2 * n + 1] + r) * stride;
                        }
                        face[14] = score;
                        if (score < ScoreThreshold) continue;
                        faces.Add(face);
                    }
            }
            if (faces.Count == 0) return null;
            if (faces.Count > 1)
            {
                var boxes = faces.Select(f => new Rect((int)f[0], (int)f[1], (int)f[2], (int)f[3])).ToArray();
                var scores = faces.Select(f => f[14]).ToArray();
                CvDnn.NMSBoxes(boxes, scores, ScoreThreshold, NmsThreshold, out int[] keep, 1f, TopK);
                faces = keep.Select(k => faces[k]).ToList();
                if (faces.Count == 0) return null;
            }
            var flat = new float[faces.Count * 15];
            for (int k = 0; k < faces.Count; k++) Array.Copy(faces[k], 0, flat, k * 15, 15);
            return NDArray.FromArray(flat, faces.Count, 15);
        }

        public void Dispose() => _net.Dispose();
    }
}
