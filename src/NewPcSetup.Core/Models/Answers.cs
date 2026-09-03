namespace NewPcSetup.Core.Models;

public enum Usage { Office, Dev, Gaming, Home }
public enum UiStyle { Win11Default, Win10Like }
public enum ImeMode { ChineseDefault, EnglishDefault }

public sealed record Answers(
    Usage Usage,
    string? DataDrive,
    bool CreatePartition,
    UiStyle UiStyle,
    bool DarkMode,
    ImeMode ImeMode,
    bool KeepShiftSwitch,
    bool DisablePromotions)
{
    public static Answers Default(EnvironmentSnapshot s) => new(
        Usage: Usage.Home,
        DataDrive: s.DataDrive,
        CreatePartition: false,
        UiStyle: UiStyle.Win11Default,
        DarkMode: false,
        ImeMode: ImeMode.ChineseDefault,
        KeepShiftSwitch: true,
        DisablePromotions: false);
}
