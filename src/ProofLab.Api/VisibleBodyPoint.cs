namespace ProofLab.Api;

/// <summary>Local consistency checks for an observed visible body surface; never a replacement for visual evidence.</summary>
public static class VisibleBodyPointPolicy
{
    public const string Accepted="VISIBLE_BODY_INTERIOR";

    public static string Eligibility(AssemblyBodyInteriorPoint point,KnotFrame frame)
    {
        if(point.MaterialPoint!="BODY")return "MATERIAL_IS_NOT_BODY";
        if(!point.Visible || point.Confidence<.8)return "INITIAL_POINT_UNCERTAIN";
        if(point.Frame!=0 || frame.Index!=0 || frame.Width<2 || frame.Height<2 ||
           new[]{point.X,point.Y,point.Confidence}.Any(v=>!double.IsFinite(v)) ||
           point.X is <0 or >=1000 || point.Y is <0 or >=1000 || point.Confidence>1)
            return "INVALID_POINT_GEOMETRY";
        if(point.VisibleBodyRegion is not {} r || new[]{r.X1,r.Y1,r.X2,r.Y2}.Any(v=>!double.IsFinite(v)||v<0||v>1000) ||
           r.X1>=r.X2 || r.Y1>=r.Y2)return "VISIBLE_REGION_UNRESOLVED";
        var x1=r.X1*frame.Width/1000;var x2=r.X2*frame.Width/1000;
        var y1=r.Y1*frame.Height/1000;var y2=r.Y2*frame.Height/1000;
        if(x2-x1<4 || y2-y1<4)return "VISIBLE_REGION_TOO_SMALL";
        // Test the exact native pixel that SAM will receive, including its final rounding.
        var px=Math.Floor(point.X*frame.Width/1000);var py=Math.Floor(point.Y*frame.Height/1000);
        var margin=Math.Max(2,.05*Math.Min(x2-x1,y2-y1));
        if(px<x1+margin || px>x2-margin || py<y1+margin || py>y2-margin)
            return "POINT_NOT_INSIDE_VISIBLE_SURFACE";
        if(point.InitialCandidate is not {Frame:0,Role:"object",Visible:true,Confidence:>=.8} c ||
           new[]{c.X1,c.Y1,c.X2,c.Y2,c.Confidence}.Any(v=>!double.IsFinite(v)) ||
           c.X1<0 || c.Y1<0 || c.X2>1000 || c.Y2>1000 || c.X2<=c.X1 || c.Y2<=c.Y1 || c.Confidence>1)
            return "INITIAL_CANDIDATE_UNRESOLVED";
        var left=c.X1*frame.Width/1000;var right=c.X2*frame.Width/1000;
        var top=c.Y1*frame.Height/1000;var bottom=c.Y2*frame.Height/1000;
        var marginX=Math.Max(2,.1*(right-left));var marginY=Math.Max(2,.1*(bottom-top));
        if(x1<left-marginX || x2>right+marginX || y1<top-marginY || y2>bottom+marginY)
            return "VISIBLE_SURFACE_DISAGREES_WITH_INITIAL_LOCATION";
        return Accepted;
    }
}
