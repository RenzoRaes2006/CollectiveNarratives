using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace CollectiveNarratives.Web;

// Het adres waar telefoons naartoe moeten (voor de QR en de links).
// Online: PUBLIC_URL=https://collective-narratives.app. Lokaal zonder PUBLIC_URL: het adres van het verzoek,
// maar "localhost" wordt het wifi-adres van deze laptop, anders werkt de QR niet op een telefoon.
public sealed class PublicUrl(IConfiguration config)
{
    readonly string? _configured = config["PUBLIC_URL"]?.TrimEnd('/');

    public string For(HttpContext? http)
    {
        if (!string.IsNullOrEmpty(_configured)) return _configured;
        if (http is null) return "http://localhost:5000";

        var req = http.Request;
        string host = req.Host.Host;
        if (host is "localhost" or "127.0.0.1" or "[::1]" && LanIp() is { } lan) host = lan;
        return req.Host.Port is { } port ? $"{req.Scheme}://{host}:{port}" : $"{req.Scheme}://{host}";
    }

    // LAN-adres, wifi eerst en virtuele adapters (VMware, VirtualBox, WSL, Docker...) achteraan.
    static string? LanIp()
    {
        static int Score(NetworkInterface n) =>
            n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 0
            : System.Text.RegularExpressions.Regex.IsMatch(n.Description + n.Name, "vmware|virtualbox|vethernet|hyper-v|docker|wsl", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ? 2
            : 1;

        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .OrderBy(Score)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a) && !a.ToString().StartsWith("169.254."))
            ?.ToString();
    }
}
