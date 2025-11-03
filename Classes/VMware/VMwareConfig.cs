namespace API.Classes.VMware
{
    /// <summary>
    /// 對應 appsettings.json 中的 VMwareConfig 區塊。
    /// </summary>
    public class VMwareConfig
    {
        public Dictionary<string, VMwareEnvironment> Environments { get; set; }
    }

    /// <summary>
    /// 代表單一 VMware vCenter 環境的連線設定。
    /// </summary>
    public class VMwareEnvironment
    {
        public string ApiBaseUrl { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
    }
}