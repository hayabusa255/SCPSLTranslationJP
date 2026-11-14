namespace SCPSLInstaller;

/// <summary>利用者に見せてよいメッセージを持つ、想定内のエラー。</summary>
internal sealed class InstallerException : Exception
{
    public InstallerException(string message) : base(message) { }
    public InstallerException(string message, Exception inner) : base(message, inner) { }
}
