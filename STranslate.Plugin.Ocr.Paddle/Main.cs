using OpenCvSharp;
using Sdcb.OpenVINO.PaddleOCR;
using Sdcb.OpenVINO.PaddleOCR.Models;
using Sdcb.OpenVINO.PaddleOCR.Models.Online;
using STranslate.Plugin.Ocr.Paddle.View;
using STranslate.Plugin.Ocr.Paddle.ViewModel;
using Control = System.Windows.Controls.Control;

namespace STranslate.Plugin.Ocr.Paddle;

/// <summary>
///     <see href="https://www.paddleocr.ai/main/version3.x/algorithm/PP-OCRv6/PP-OCRv6_multi_languages.html"/>
/// </summary>
public class Main : IOcrPlugin
{
    private Control? _settingUi;
    private SettingsViewModel? _viewModel;
    private Settings Settings { get; set; } = null!;
    private IPluginContext Context { get; set; } = null!;

    public IEnumerable<LangEnum> SupportedLanguages =>
    [
        LangEnum.Auto,
        LangEnum.ChineseSimplified,
        LangEnum.ChineseTraditional,
        LangEnum.English,
        LangEnum.Korean,
        LangEnum.Japanese,
    ];

    public bool SupportBoxPoints() => true;

    public Control GetSettingUI()
    {
        _viewModel ??= new SettingsViewModel(Context, Settings);
        _settingUi ??= new SettingsView { DataContext = _viewModel };
        return _settingUi;
    }

    public void Init(IPluginContext context)
    {
        Context = context;
        Settings.DefaultPath = Context.MetaData.PluginCacheDirectoryPath;
        Settings = context.LoadSettingStorage<Settings>();
    }

    public void Dispose() { }

    public async Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken)
    {
        var result = new OcrResult();

        try
        {
            // 创建超时取消令牌（默认30秒超时）
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            // 在线程池中异步执行同步的OCR操作，避免阻塞当前线程
            var ocrResult = await Task.Run(() =>
            {
                combinedCts.Token.ThrowIfCancellationRequested();
                var model = GetModel(request.Language);
                using PaddleOcrAll engine = new(model);
                using Mat src = Cv2.ImDecode(request.ImageData, ImreadModes.Color);
                return engine.Run(src);
            }, combinedCts.Token);

            if (ocrResult?.Regions != null && ocrResult.Regions.Length > 0)
            {
                foreach (var block in ocrResult.Regions)
                {
                    var ocrContent = new OcrContent() { Text = block.Text };
                    // 实现坐标转换：从 RotatedRect 获取 4 个顶点坐标
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

    private FullOcrModel GetModel(LangEnum language)
    {
        Sdcb.OpenVINO.PaddleOCR.Models.Online.Settings.GlobalModelDirectory = Settings.ModelsDirectory;
        var model = Settings.ModelSize switch
        {
            "Tiny" => OnlineFullModels.ChineseV6Tiny.DownloadAsync().GetAwaiter().GetResult(),
            "Medium" => OnlineFullModels.ChineseV6Medium.DownloadAsync().GetAwaiter().GetResult(),
            _ => OnlineFullModels.ChineseV6Small.DownloadAsync().GetAwaiter().GetResult(),
        };

        return model;
    }
}
