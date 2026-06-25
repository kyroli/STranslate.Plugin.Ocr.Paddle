using OpenCvSharp;
using Sdcb.OpenVINO.PaddleOCR;
using Sdcb.OpenVINO.PaddleOCR.Models;
using Sdcb.OpenVINO.PaddleOCR.Models.Online;
using STranslate.Plugin.Ocr.Paddle.View;
using STranslate.Plugin.Ocr.Paddle.ViewModel;
using Control = System.Windows.Controls.Control;

namespace STranslate.Plugin.Ocr.Paddle;

/// <summary>
/// PaddleOCR (V6) 离线精简定制识别插件主入口类。
/// 详细算法可参考：<see href="https://www.paddleocr.ai/main/version3.x/algorithm/PP-OCRv6/PP-OCRv6_multi_languages.html"/>
/// </summary>
public class Main : IOcrPlugin
{
    private Control? _settingUi;
    private SettingsViewModel? _viewModel;

    /// <summary>
    /// 插件配置实例
    /// </summary>
    private Settings PluginSettings { get; set; } = null!;

    /// <summary>
    /// 插件上下文接口
    /// </summary>
    private IPluginContext Context { get; set; } = null!;

    /// <summary>
    /// 插件支持的 OCR 识别语言列表
    /// </summary>
    public IEnumerable<LangEnum> SupportedLanguages =>
    [
        LangEnum.Auto,
        LangEnum.ChineseSimplified,
        LangEnum.ChineseTraditional,
        LangEnum.English,
        LangEnum.Korean,
        LangEnum.Japanese,
    ];

    /// <summary>
    /// 是否支持返回文字的包围盒坐标点 (SDK 1.0.12+ 支持)
    /// </summary>
    public bool SupportBoxPoints() => true;

    /// <summary>
    /// 获取插件的配置 UI 控件
    /// </summary>
    public Control GetSettingUI()
    {
        _viewModel ??= new SettingsViewModel(Context, PluginSettings);
        _settingUi ??= new SettingsView { DataContext = _viewModel };
        return _settingUi;
    }

    /// <summary>
    /// 初始化插件配置
    /// </summary>
    /// <param name="context">插件上下文</param>
    public void Init(IPluginContext context)
    {
        Context = context;
        // 设置静态默认路径，供 Settings 类的默认 Models 目录路径合成使用
        Settings.DefaultPath = Context.MetaData.PluginCacheDirectoryPath;
        PluginSettings = context.LoadSettingStorage<Settings>();
    }

    /// <summary>
    /// 释放插件资源
    /// </summary>
    public void Dispose() { }

    /// <summary>
    /// 执行异步 OCR 识别
    /// </summary>
    /// <param name="request">OCR 识别请求参数，包含图片数据与目标语种</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>OCR 识别结果</returns>
    public async Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken)
    {
        var result = new OcrResult();

        try
        {
            // 1. 创建关联的取消令牌，加入 30 秒超时控制
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            // 2. 在线程池中异步执行同步的 OCR 推理，防止界面卡顿
            var ocrResult = await Task.Run(() =>
            {
                combinedCts.Token.ThrowIfCancellationRequested();
                using Mat src = Cv2.ImDecode(request.ImageData, ImreadModes.Color);

                if (request.Language == LangEnum.Auto)
                {
                    // 获取对应的两个识别模型进行轻量级比对
                    var modelV6 = GetModel(LangEnum.Auto);
                    var modelV4 = GetModel(LangEnum.Korean);

                    // 2.1 中央探针算法：裁剪图片中央最多 100 像素高度的窄条，用于低消耗语种探测
                    int probeHeight = Math.Min(src.Height, 100);
                    int probeY = (src.Height - probeHeight) / 2;
                    using Mat probeSrc = new(src, new Rect(0, probeY, src.Width, probeHeight));

                    // 2.2 在探针区域初始化轻量引擎并运行快速识别（仅需数毫秒）
                    using var engineV6 = new PaddleOcrAll(modelV6);
                    using var engineV4 = new PaddleOcrAll(modelV4);

                    var resV6 = engineV6.Run(probeSrc);
                    var resV4 = engineV4.Run(probeSrc);

                    // 辅助函数：统计识别文本中有效的字母和文字字符数（过滤标点符号与数字的干扰）
                    int GetValidCharCount(PaddleOcrResult? res)
                    {
                        if (res?.Regions == null) return 0;
                        int count = 0;
                        foreach (var r in res.Regions)
                        {
                            if (string.IsNullOrEmpty(r.Text)) continue;
                            foreach (char c in r.Text)
                            {
                                if (char.IsLetter(c) || 
                                    (c >= 0x4e00 && c <= 0x9fa5) || 
                                    (c >= 0xac00 && c <= 0xd7a3) || 
                                    (c >= 0x3040 && c <= 0x30ff))
                                {
                                    count++;
                                }
                            }
                        }
                        return count;
                    }

                    int countV6 = GetValidCharCount(resV6);
                    int countV4 = GetValidCharCount(resV4);

                    // 2.3 比对识别出的有效字数，决定最终对原图推理的引擎 (韩文 V4 胜出则用 V4，否则用 V6)
                    if (countV4 > countV6)
                    {
                        return engineV4.Run(src);
                    }
                    else
                    {
                        return engineV6.Run(src);
                    }
                }
                else
                {
                    var model = GetModel(request.Language);
                    using PaddleOcrAll engine = new(model);
                    return engine.Run(src);
                }
            }, combinedCts.Token);

            // 3. 组装并转换识别结果
            if (ocrResult?.Regions != null && ocrResult.Regions.Length > 0)
            {
                foreach (var block in ocrResult.Regions)
                {
                    var ocrContent = new OcrContent() { Text = block.Text };
                    
                    // 转换文本块包围盒坐标：将 RotatedRect 转换为 4 个顶点的 BoxPoint 列表
                    var points = block.Rect.Points();
                    foreach (var point in points)
                    {
                        ocrContent.BoxPoints.Add(new BoxPoint(point.X, point.Y));
                    }
                    result.OcrContents.Add(ocrContent);
                }
                return result;
            }
            return result.Fail("识别结果为空");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return result.Fail("操作已被取消");
        }
        catch (OperationCanceledException)
        {
            return result.Fail("识别操作超时（30秒）");
        }
        catch (Exception ex)
        {
            return result.Fail($"识别过程中发生错误: {ex.Message}");
        }
    }

    /// <summary>
    /// 获取指定语言对应的 PaddleOCR 模型。
    /// 如果本地未缓存，会自动下载对应模型。
    /// </summary>
    /// <param name="language">目标识别语种</param>
    /// <returns>已就绪的 FullOcrModel 实例</returns>
    private FullOcrModel GetModel(LangEnum language)
    {
        // 配置 OpenVINO PaddleOCR 模型全局下载与读取目录
        Sdcb.OpenVINO.PaddleOCR.Models.Online.Settings.GlobalModelDirectory = PluginSettings.ModelsDirectory;
        
        // 韩语使用 KoreanV4 模型，其他所有语言统一映射至 ChineseV6Small
        var onlineModel = language == LangEnum.Korean 
            ? OnlineFullModels.KoreanV4 
            : OnlineFullModels.ChineseV6Small;

        return onlineModel.DownloadAsync().GetAwaiter().GetResult();
    }
}
