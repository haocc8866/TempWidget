namespace TempWidget.Models;

public record TempReading(float? Cpu, float? Gpu, DateTime Timestamp)
{
    public bool HasCpu => Cpu.HasValue;
    public bool HasGpu => Gpu.HasValue;
}
