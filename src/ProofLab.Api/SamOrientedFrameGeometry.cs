namespace ProofLab.Api;

/// <summary>Displayed raster geometry. Pixel hashes still verify the actual
/// orientation/content; equal dimensions alone never certify a clip.</summary>
internal readonly record struct SamOrientedFrameGeometry(int Width,int Height)
{
    internal static SamOrientedFrameGeometry FromStored(int width,int height,int rotation)
    {
        if(width is <2 or >8192 || height is <2 or >8192 || rotation%90!=0)
            throw new InvalidDataException("Unsupported source display geometry.");
        return rotation%180==0?new(width,height):new(height,width);
    }
}
