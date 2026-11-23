using System.Runtime.InteropServices;
using System.Text;

namespace WiFiBandSelector;

/// <summary>État radio réel de l'interface, tel que le pilote et Windows le voient.</summary>
public sealed record WlanStatus(
    bool Connected,
    string? Ssid,
    string? ProfileName,
    string? Bssid,
    int FrequencyMhz,
    Band? Band)
{
    public static WlanStatus Disconnected { get; } = new(false, null, null, null, 0, null);
}

/// <summary>
/// Accès minimal à wlanapi.dll : état de la connexion, fréquence réelle du point d'accès,
/// analyse des réseaux visibles, reconnexion. Aucune sortie localisée n'est analysée.
/// </summary>
public static class WlanApi
{
    private const uint ClientVersion = 2; // Windows Vista et suivants
    private const int ErrorSuccess = 0;
    private const int MaxProfileNameChars = 256;

    private enum WlanIntfOpcode
    {
        AutoconfEnabled = 1,
        BackgroundScanEnabled = 2,
        MediaStreamingMode = 3,
        RadioState = 4,
        BssType = 5,
        InterfaceState = 6,
        CurrentConnection = 7,
        ChannelNumber = 8,
    }

    private enum Dot11BssType
    {
        Infrastructure = 1,
        Independent = 2,
        Any = 3,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Dot11Ssid
    {
        public uint Length;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] Bytes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Dot11MacAddress
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public byte[] Bytes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WlanAssociationAttributes
    {
        public Dot11Ssid Ssid;
        public int BssType;
        public Dot11MacAddress Bssid;
        public int PhyType;
        public uint PhyIndex;
        public uint SignalQuality;
        public uint RxRate;
        public uint TxRate;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WlanSecurityAttributes
    {
        public int SecurityEnabled;
        public int OneXEnabled;
        public int AuthAlgorithm;
        public int CipherAlgorithm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WlanConnectionAttributes
    {
        public int State;
        public int ConnectionMode;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MaxProfileNameChars)]
        public string ProfileName;

        public WlanAssociationAttributes Association;
        public WlanSecurityAttributes Security;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WlanRateSet
    {
        public uint RateSetLength;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 126)]
        public ushort[] Rates;
    }

    /// <summary>
    /// Définition réelle de WLAN_BSS_ENTRY (16 champs, 360 octets).
    /// Attention : de vieux extraits de documentation décrivent une structure avec
    /// ulChmFrequency / bSecurityEnabled ; elle ne correspond plus à wlanapi.dll.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct WlanBssEntry
    {
        public Dot11Ssid Ssid;                  // 0
        public uint PhyId;                      // 36
        public Dot11MacAddress Bssid;           // 40
        public int BssType;                     // 48
        public int PhyType;                     // 52
        public int Rssi;                        // 56
        public uint LinkQuality;                // 60
        public byte InRegDomain;                // 64
        public ushort BeaconPeriod;             // 66
        public ulong Timestamp;                 // 72
        public ulong HostTimestamp;             // 80
        public ushort CapabilityInformation;    // 88
        public uint CenterFrequencyKhz;         // 92
        public WlanRateSet RateSet;             // 96
        public uint IeOffset;                   // 352
        public uint IeSize;                     // 356
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WlanBssListHeader
    {
        public uint TotalSize;
        public uint NumberOfItems;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WlanConnectionParameters
    {
        public int ConnectionMode;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string Profile;

        public IntPtr DesiredSsid;
        public IntPtr DesiredBssidList;
        public int BssType;
        public uint Flags;
    }

    /// <summary>Ordre réel de la API : version négociée AVANT le handle.</summary>
    [DllImport("wlanapi.dll")]
    private static extern int WlanOpenHandle(
        uint clientVersion, IntPtr reserved, out uint negotiatedVersion, out IntPtr handle);

    [DllImport("wlanapi.dll")]
    private static extern int WlanCloseHandle(IntPtr handle, IntPtr reserved);

    [DllImport("wlanapi.dll")]
    private static extern void WlanFreeMemory(IntPtr memory);

    [DllImport("wlanapi.dll")]
    private static extern int WlanQueryInterface(
        IntPtr handle,
        ref Guid interfaceGuid,
        WlanIntfOpcode opcode,
        IntPtr reserved,
        out int dataSize,
        out IntPtr data,
        out int opcodeValueType);

    [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)]
    private static extern int WlanConnect(
        IntPtr handle,
        ref Guid interfaceGuid,
        ref WlanConnectionParameters parameters,
        IntPtr reserved);

    [DllImport("wlanapi.dll")]
    private static extern int WlanDisconnect(IntPtr handle, ref Guid interfaceGuid, IntPtr reserved);

    [DllImport("wlanapi.dll")]
    private static extern int WlanScan(
        IntPtr handle, ref Guid interfaceGuid, IntPtr ssid, IntPtr ieData, IntPtr reserved);

    [DllImport("wlanapi.dll")]
    private static extern int WlanGetNetworkBssList(
        IntPtr handle,
        ref Guid interfaceGuid,
        IntPtr ssid,
        Dot11BssType bssType,
        int securityEnabled,
        IntPtr reserved,
        out IntPtr bssList);

    /// <summary>État de la connexion et bande réellement utilisée.</summary>
    public static WlanStatus QueryStatus(Guid interfaceGuid)
    {
        var attributes = TryGetConnectionAttributes(interfaceGuid);
        if (attributes is null)
            return WlanStatus.Disconnected;

        var attrs = attributes.Value;
        var ssid = DecodeSsid(attrs.Association.Ssid);
        var bssid = FormatBssid(attrs.Association.Bssid.Bytes);

        var frequency = GetBssFrequency(interfaceGuid, attrs.Association.Bssid.Bytes);
        var band = BandInfo.FromFrequencyMhz(frequency);
        if (band is null && frequency <= 0)
            band = BandFromChannel(GetChannel(interfaceGuid));

        return new WlanStatus(
            Connected: true,
            Ssid: ssid,
            ProfileName: attrs.ProfileName,
            Bssid: bssid,
            FrequencyMhz: frequency,
            Band: band);
    }

    /// <summary>Bandes visibles dans le cache des réseaux analysés.</summary>
    public static HashSet<Band> ScanBands(Guid interfaceGuid, int waitMs)
    {
        var handle = IntPtr.Zero;
        try
        {
            if (!TryOpen(out handle, out _))
                return new HashSet<Band>();

            var guid = interfaceGuid;
            WlanScan(handle, ref guid, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        }
        finally
        {
            if (handle != IntPtr.Zero)
                WlanCloseHandle(handle, IntPtr.Zero);
        }

        if (waitMs > 0)
            Thread.Sleep(waitMs);

        var bands = new HashSet<Band>();
        foreach (var entry in GetBssEntries(interfaceGuid))
        {
            var band = BandInfo.FromFrequencyMhz(entry.FrequencyMhz);
            if (band is not null)
                bands.Add(band.Value);
        }

        return bands;
    }

    /// <summary>Déconnexion puis reconnexion au profil courant. Faux si impossible.</summary>
    public static bool Reconnect(Guid interfaceGuid, string profileName)
    {
        var handle = IntPtr.Zero;
        try
        {
            if (!TryOpen(out handle, out _))
                return false;

            var guid = interfaceGuid;
            WlanDisconnect(handle, ref guid, IntPtr.Zero);
            Thread.Sleep(500);

            var parameters = new WlanConnectionParameters
            {
                ConnectionMode = 0, // wlan_connection_mode_profile
                Profile = profileName,
                DesiredSsid = IntPtr.Zero,
                DesiredBssidList = IntPtr.Zero,
                BssType = (int)Dot11BssType.Infrastructure,
                Flags = 0,
            };

            var rc = WlanConnect(handle, ref guid, ref parameters, IntPtr.Zero);
            if (rc != ErrorSuccess)
                Logger.Write($"WlanConnect a échoué pour « {profileName} » (code {rc})");

            return rc == ErrorSuccess;
        }
        finally
        {
            if (handle != IntPtr.Zero)
                WlanCloseHandle(handle, IntPtr.Zero);
        }
    }

    private static WlanConnectionAttributes? TryGetConnectionAttributes(Guid interfaceGuid)
    {
        var handle = IntPtr.Zero;
        var data = IntPtr.Zero;
        try
        {
            if (!TryOpen(out handle, out _))
                return null;

            var guid = interfaceGuid;
            var rc = WlanQueryInterface(
                handle, ref guid, WlanIntfOpcode.CurrentConnection, IntPtr.Zero,
                out _, out data, out _);

            if (rc != ErrorSuccess || data == IntPtr.Zero)
                return null;

            var attrs = Marshal.PtrToStructure<WlanConnectionAttributes>(data);

            // wlan_interface_state_connected == 1
            return attrs.State == 1 ? attrs : null;
        }
        catch (Exception ex)
        {
            Logger.Write($"Lecture de la connexion impossible : {ex.Message}");
            return null;
        }
        finally
        {
            if (data != IntPtr.Zero)
                WlanFreeMemory(data);
            if (handle != IntPtr.Zero)
                WlanCloseHandle(handle, IntPtr.Zero);
        }
    }

    private static int GetBssFrequency(Guid interfaceGuid, byte[] bssid)
    {
        foreach (var entry in GetBssEntries(interfaceGuid))
        {
            if (!entry.Bssid.SequenceEqual(bssid))
                continue;

            return entry.FrequencyMhz;
        }

        return 0;
    }

    private static List<(byte[] Bssid, int FrequencyMhz)> GetBssEntries(Guid interfaceGuid)
    {
        var result = new List<(byte[], int)>();
        var handle = IntPtr.Zero;
        var list = IntPtr.Zero;

        try
        {
            if (!TryOpen(out handle, out _))
                return result;

            var guid = interfaceGuid;
            var rc = WlanGetNetworkBssList(
                handle, ref guid, IntPtr.Zero, Dot11BssType.Any, 0, IntPtr.Zero, out list);

            if (rc != ErrorSuccess || list == IntPtr.Zero)
            {
                if (rc != ErrorSuccess)
                    Logger.Write($"WlanGetNetworkBssList a échoué (code {rc})");
                return result;
            }

            var header = Marshal.PtrToStructure<WlanBssListHeader>(list);
            var stride = Marshal.SizeOf<WlanBssEntry>();
            var count = (int)Math.Min(header.NumberOfItems, 512);

            for (var i = 0; i < count; i++)
            {
                var entryPtr = IntPtr.Add(list, 8 + i * stride);
                var entry = Marshal.PtrToStructure<WlanBssEntry>(entryPtr);

                // ulChCenterFrequency est en kHz (ex. 5 180 000 pour le canal 36).
                var mhz = entry.CenterFrequencyKhz / 1000;
                if (mhz is < 2000 or > 7125)
                    continue;

                var bssid = entry.Bssid.Bytes ?? Array.Empty<byte>();
                result.Add((bssid, (int)mhz));
            }
        }
        catch (Exception ex)
        {
            Logger.Write($"Analyse des points d'accès impossible : {ex.Message}");
        }
        finally
        {
            if (list != IntPtr.Zero)
                WlanFreeMemory(list);
            if (handle != IntPtr.Zero)
                WlanCloseHandle(handle, IntPtr.Zero);
        }

        return result;
    }

    private static int GetChannel(Guid interfaceGuid)
    {
        var handle = IntPtr.Zero;
        var data = IntPtr.Zero;
        try
        {
            if (!TryOpen(out handle, out _))
                return -1;

            var guid = interfaceGuid;
            var rc = WlanQueryInterface(
                handle, ref guid, WlanIntfOpcode.ChannelNumber, IntPtr.Zero,
                out _, out data, out _);

            return rc == ErrorSuccess && data != IntPtr.Zero ? Marshal.ReadInt32(data) : -1;
        }
        finally
        {
            if (data != IntPtr.Zero)
                WlanFreeMemory(data);
            if (handle != IntPtr.Zero)
                WlanCloseHandle(handle, IntPtr.Zero);
        }
    }

    /// <summary>Dernier recours quand la liste des points d'accès est indisponible.</summary>
    private static Band? BandFromChannel(int channel) => channel switch
    {
        < 0 => null,
        <= 14 => Band.Band24,
        <= 196 => Band.Band5,
        _ => Band.Band6,
    };

    private static bool TryOpen(out IntPtr handle, out uint negotiatedVersion)
    {
        var rc = WlanOpenHandle(ClientVersion, IntPtr.Zero, out negotiatedVersion, out handle);
        if (rc == ErrorSuccess)
            return true;

        Logger.Write($"WlanOpenHandle a échoué (code {rc})");
        handle = IntPtr.Zero;
        return false;
    }

    private static string DecodeSsid(Dot11Ssid ssid)
    {
        var bytes = ssid.Bytes ?? Array.Empty<byte>();
        var length = (int)Math.Min(Math.Max(ssid.Length, 0), (uint)bytes.Length);
        return length == 0 ? string.Empty : Encoding.UTF8.GetString(bytes, 0, length);
    }

    private static string FormatBssid(byte[]? bytes) =>
        bytes is null || bytes.Length < 6
            ? string.Empty
            : string.Join(":", bytes.Take(6).Select(b => b.ToString("X2")));
}
