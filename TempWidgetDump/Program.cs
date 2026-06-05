using System.Text;
using LibreHardwareMonitor.Hardware;

Console.OutputEncoding = Encoding.UTF8;
Console.WriteLine("========================================");
Console.WriteLine("TempWidget Sensor Dump");
Console.WriteLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
Console.WriteLine($"OS:   {Environment.OSVersion}");
Console.WriteLine($"CPU cores: {Environment.ProcessorCount}");
Console.WriteLine("========================================\n");

var computer = new Computer
{
    IsCpuEnabled = true,
    IsGpuEnabled = true,
    IsMotherboardEnabled = true,
    IsControllerEnabled = true,
    IsStorageEnabled = false,
    IsNetworkEnabled = false,
    IsMemoryEnabled = false,
};

try
{
    computer.Open();
    Console.WriteLine("Computer opened OK.\n");
}
catch (Exception ex)
{
    Console.WriteLine($"[FATAL] Computer.Open failed: {ex.Message}");
    return 1;
}

int hardwareCount = 0;
int sensorCount = 0;

foreach (var hw in computer.Hardware)
{
    hardwareCount++;
    try { hw.Update(); } catch (Exception ex) { Console.WriteLine($"  [WARN] {hw.Name} update failed: {ex.Message}"); }

    Console.WriteLine($"[{hw.HardwareType}] {hw.Name}");
    foreach (var sensor in hw.Sensors)
    {
        sensorCount++;
        var val = sensor.Value.HasValue ? $"{sensor.Value.Value:F1}" : "(null)";
        Console.WriteLine($"  [{sensor.SensorType,-13}] {sensor.Name,-30} = {val}");
    }
    Console.WriteLine();
}

Console.WriteLine("========================================");
Console.WriteLine($"Total: {hardwareCount} hardware, {sensorCount} sensors");
Console.WriteLine("========================================");

computer.Close();
Console.WriteLine("\n按任意键退出...");
Console.ReadKey();
return 0;
