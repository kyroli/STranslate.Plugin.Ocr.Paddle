using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace STranslate.Plugin.Ocr.Paddle.ViewModel;

/// <summary>
/// 插件设置视图模型
/// </summary>
/// <param name="context">插件上下文</param>
/// <param name="settings">插件配置实例</param>
public partial class SettingsViewModel(IPluginContext context, Settings settings) : ObservableObject
{
    /// <summary>
    /// 模型存放目录路径
    /// </summary>
    [ObservableProperty] public partial string ModelsDirectory { get; set; } = settings.ModelsDirectory;

    /// <summary>
    /// 当模型目录路径变更时触发，自动保存配置至存储
    /// </summary>
    partial void OnModelsDirectoryChanged(string value)
    {
        settings.ModelsDirectory = value;
        context.SaveSettingStorage<Settings>();
    }

    /// <summary>
    /// 打开文件夹选择对话框，更新模型存放路径
    /// </summary>
    [RelayCommand]
    private void SelectFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Multiselect = false,
            RootFolder = Environment.SpecialFolder.DesktopDirectory,
        };
        if (dialog.ShowDialog() != DialogResult.OK)
            return;

        ModelsDirectory = dialog.SelectedPath;
    }
}