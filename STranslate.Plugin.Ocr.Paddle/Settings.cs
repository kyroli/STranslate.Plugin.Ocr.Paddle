using System.IO;

namespace STranslate.Plugin.Ocr.Paddle;

/// <summary>
/// 插件配置管理类
/// </summary>
public class Settings
{
    private static string _defaultPath = string.Empty;

    /// <summary>
    /// 获取或设置静态默认路径。若目标路径目录不存在，将自动创建该目录。
    /// </summary>
    public static string DefaultPath
    {
        get => _defaultPath;
        set
        {
            if (!Directory.Exists(value))
                Directory.CreateDirectory(value);

            _defaultPath = value;
        }
    }

    /// <summary>
    /// 获取或设置模型下载存放目录路径
    /// </summary>
    public string ModelsDirectory { get; set; } = Path.Combine(DefaultPath, "Models");
}