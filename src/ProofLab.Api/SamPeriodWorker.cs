using System.Diagnostics;
using System.Text;
using System.Text.Json;
namespace ProofLab.Api;

public sealed class SamPeriodWorker(IConfiguration config):ISamPeriodWorker
{
    private readonly SemaphoreSlim gate=new(1,1);
    public async Task<string> MeasureAsync(object input,CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(90));
        using var process=new Process();
        try {
            var payload=JsonSerializer.Serialize(input,new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if(Encoding.UTF8.GetByteCount(payload)>32_000_000)throw new ArgumentException("Worker input limit.");
            var worker=Path.Combine(AppContext.BaseDirectory,"Workers","sam-period-worker.cjs");
            if(!File.Exists(worker))throw new FileNotFoundException("Build the local SAM measurement worker.");
            var start=new ProcessStartInfo(config["Research:NodeExecutable"]??"node") {
                UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,
                RedirectStandardOutput=true,RedirectStandardError=true,
                StandardInputEncoding=new UTF8Encoding(false),StandardOutputEncoding=Encoding.UTF8
            };
            start.ArgumentList.Add("--max-old-space-size=512");start.ArgumentList.Add(worker);
            start.Environment.Remove("MODEL_API_KEY"); // the measurement worker needs no credentials
            process.StartInfo=start;process.Start();
            var output=ReadBounded(process.StandardOutput,3_000_000,deadline.Token);
            var error=ReadBounded(process.StandardError,4096,deadline.Token);
            await process.StandardInput.WriteAsync(payload.AsMemory(),deadline.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(deadline.Token);
            var result=await output;await error;
            if(process.ExitCode!=0)throw new InvalidDataException("Authenticated SAM measurement failed.");
            return result;
        }
        finally {
            try {if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}
            gate.Release();
        }
    }
    private static async Task<string> ReadBounded(StreamReader reader,int max,CancellationToken ct)
    {
        var result=new StringBuilder();var chars=new char[8192];int n;
        while((n=await reader.ReadAsync(chars.AsMemory(),ct))>0) {
            if(result.Length+n>max)throw new InvalidDataException("Worker output limit.");
            result.Append(chars,0,n);
        }
        return result.ToString();
    }
}
