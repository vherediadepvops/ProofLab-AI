using ProofLab.Api;
namespace ProofLab.Tests;
public sealed class SamOrientedFrameGeometryTests
{
    [Theory]
    [InlineData(-90,1080,1920)]
    [InlineData(90,1080,1920)]
    [InlineData(270,1080,1920)]
    [InlineData(0,1920,1080)]
    [InlineData(180,1920,1080)]
    [InlineData(360,1920,1080)]
    public void StoredRotationMatchesDisplayedRaster(int rotation,int width,int height)
    {
        Assert.Equal(SamOrientedFrameGeometry.FromStored(width,height,0),
            SamOrientedFrameGeometry.FromStored(1920,1080,rotation));
    }
    [Fact] public void ResizedRasterIsNotAccepted()
    {
        Assert.NotEqual(SamOrientedFrameGeometry.FromStored(1920,1080,-90),
            SamOrientedFrameGeometry.FromStored(720,1280,0));
    }
    [Theory]
    [InlineData(0,1080,0)]
    [InlineData(1920,9000,0)]
    [InlineData(1920,1080,45)]
    public void ResamplingOrUnsafeDimensionsFail(int width,int height,int rotation)
    {
        Assert.Throws<InvalidDataException>(()=>SamOrientedFrameGeometry.FromStored(width,height,rotation));
    }
}
