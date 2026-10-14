using MoonTTS.Domain.Entities;

namespace MoonTTS.CLI.commands.Output;

/* Helper to get device by ID, name, or sub name */

internal static class DeviceFinder
{
    public static AudioDeviceInfo? FindDevice(List<AudioDeviceInfo> devices, string query)
    {
        var byId = devices.FirstOrDefault(
            d => 
                string.Equals(
                    d.DeviceId, 
                    query, 
                    StringComparison.OrdinalIgnoreCase));
        if (byId != null) return byId;
        
        var byExactName = devices.FirstOrDefault(
            d => 
                string.Equals(
                    d.DeviceName, 
                    query, 
                    StringComparison.OrdinalIgnoreCase));  
        if  (byExactName != null) return byExactName;

        var bySubName = devices.FirstOrDefault(d =>
            d.DeviceName.Contains(query, StringComparison.OrdinalIgnoreCase));
        if (bySubName != null) return bySubName;
        return null;
    }
}