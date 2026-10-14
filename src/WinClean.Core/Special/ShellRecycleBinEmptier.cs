using System.Runtime.InteropServices;

namespace WinClean.Core.Special;

/// <summary>
/// Empties the Recycle Bin exclusively through the Windows Shell API (SHEmptyRecycleBin) for
/// all drives. The bin's on-disk folders are never touched manually, and no confirmation,
/// progress dialog or sound is raised — the user already confirmed inside WinClean.
/// </summary>
public sealed class ShellRecycleBinEmptier : IRecycleBinEmptier
{
    private const uint SherbNoConfirmation = 0x00000001;
    private const uint SherbNoProgressUi = 0x00000002;
    private const uint SherbNoSound = 0x00000004;
    private const int HResultOk = 0;
    private const int HResultUnexpected = unchecked((int)0x8000FFFF);

    public string? Empty()
    {
        try
        {
            int hresult = SHEmptyRecycleBin(
                IntPtr.Zero,
                pszRootPath: null,
                SherbNoConfirmation | SherbNoProgressUi | SherbNoSound);

            if (hresult == HResultOk)
            {
                return null;
            }

            if (hresult == HResultUnexpected)
            {
                // E_UNEXPECTED: the Recycle Bin is already empty — nothing to do, report success.
                return null;
            }

            return $"the Shell API returned error 0x{hresult:X8}";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);
}
