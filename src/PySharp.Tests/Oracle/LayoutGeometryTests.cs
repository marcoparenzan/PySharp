namespace PySharp.Tests.Oracle;
[Collection("oracle-process-state")]
public class LayoutGeometryTests
{
    [Fact]
    public void ColorbarLayoutMatchesIndependentUnroundedMatplotlibFixture()
    {
        // CPython/Matplotlib fixture on this host (ops/ProbeLayoutGeometry.cs).
        // 5e-4 normalized coordinates = 0.32px horizontally / 0.24px vertically;
        // allow subpixel font rasterizer differences, never double-round the coordinates.
        var result=Py.Run("""
import numpy as np
import matplotlib.pyplot as plt
fig, ax = plt.subplots()
im = ax.imshow(np.arange(12).reshape(3, 4))
fig.colorbar(im, ax=ax)
fig.tight_layout()
expected = [0.09175347222222223, 0.15504799171730327, 0.7078472222222222, 0.7078472222222221]
actual = ax.get_position().bounds
print(all(abs(float(a) - e) < 0.0005 for a, e in zip(actual, expected)))
""");
        Assert.Equal("True\n",result);
    }
}