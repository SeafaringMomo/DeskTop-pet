using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media;
using DesktopPetCSharp.Services;
using DesktopPetCSharp.Services.AI;

namespace DesktopPetCSharp.Generators;

/// <summary>
/// AI 精灵图助手（方向 7）：用 AI 对话生成像素画字符网格代码。
///
/// 工作流：
/// 1. 用户输入角色描述（如 "橙色头发的小女孩，戴草帽，大眼睛"）
/// 2. BuildPrompt 把描述 + 调色板 + 帧规格组装成 AI 提示词
/// 3. 调用 IAiProvider.ChatAsync 获取 AI 生成的字符网格代码
/// 4. ParseFrames 从 AI 回复中提取 64×64 字符网格
/// 5. PixelArtRenderer.RenderSpriteSheetGrid 渲染成 PNG
///
/// 复用现有 AI 基础设施（IAiProvider / AiProviderRegistry），
/// 不依赖图像生成 API（DALL-E/SDXL），因为：
/// - 图像生成 API 输出是栅格化 PNG，无法精确控制 64×64 像素
/// - 字符网格代码可读、可编辑、可版本控制，便于人工微调
/// - 复用对话接口，无需新增配置项
///
/// 帧规格：64×64 像素 × 6×5=30 帧（与 LuffySpriteGenerator 保持一致）。
/// 表情布局：0=睁眼 1=闭眼 2=微笑 3=张嘴 4=撇眉 5=半眯眼
///          6=大笑 7=红脸 8-10=过渡 11=蓄泪 12=流泪 13=惊讶
///          14=红脸渐变 15=张大嘴 16=困倦 17=说话
///          18-25=8 方向看向 26=看前 27=生气瞪眼 28=得意 29=预留
///
/// 用途：快速生成角色初稿，再用 Aseprite 或手工修正细节。
/// </summary>
public static class AiSpriteAssistant
{
    /// <summary>默认帧规格：64×64 像素，2 倍放大输出 128×128，6×5=30 帧。</summary>
    public const int DefaultFramePx = 64;
    public const int DefaultScale = 2;
    public const int DefaultCols = 6;
    public const int DefaultRows = 5;

    /// <summary>构建让 AI 生成像素画字符网格的提示词。</summary>
    /// <param name="characterDescription">角色外貌描述（如 "草帽海贼团船长，黑色短发，红色背心，蓝色短裤"）</param>
    /// <param name="palette">调色板（字符 → 颜色），从角色生成器获取</param>
    public static string BuildPrompt(string characterDescription, Dictionary<char, Color> palette)
    {
        var sb = new StringBuilder();
        sb.AppendLine("你是一位像素画艺术家。请根据以下描述，用字符网格画一个 32×32 像素的角色。");
        sb.AppendLine();
        sb.AppendLine($"## 角色描述");
        sb.AppendLine(characterDescription);
        sb.AppendLine();
        sb.AppendLine("## 调色板（字符 → 颜色含义）");
        foreach (var kv in palette)
        {
            var c = kv.Value;
            if (c.A == 0)
                sb.AppendLine($"  '.' = 透明（背景）");
            else
                sb.AppendLine($"  '{kv.Key}' = #{c.R:X2}{c.G:X2}{c.B:X2}");
        }
        sb.AppendLine();
        sb.AppendLine("## 格式要求");
        sb.AppendLine($"1. 输出 {DefaultFramePx} 行，每行 {DefaultFramePx} 个字符");
        sb.AppendLine("2. 只使用调色板中定义的字符，未定义的字符视为透明");
        sb.AppendLine("3. 角色应在 32×32 网格中居中，上下左右留少量透明边距");
        sb.AppendLine("4. 用代码块包裹输出，标记为 ```pixels");
        sb.AppendLine("5. 不要添加任何解释文字，只输出字符网格");
        sb.AppendLine();
        sb.AppendLine("## 示例格式（仅展示格式，不是你要画的内容）");
        sb.AppendLine("```pixels");
        sb.AppendLine("................................");
        sb.AppendLine("..........TTTTTT..............");
        sb.AppendLine(".........TTTTTTTT.............");
        sb.AppendLine($"...（共 {DefaultFramePx} 行）...");
        sb.AppendLine("................................");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine($"现在请根据描述「{characterDescription}」生成 {DefaultFramePx}×{DefaultFramePx} 像素画字符网格。");
        return sb.ToString();
    }

    /// <summary>从 AI 回复中解析字符网格。
    /// 查找 ```pixels ... ``` 代码块，提取每行字符。
    /// 验证行数 = framePx 且每行长度 = framePx，不足则补透明 '.'。</summary>
    public static string[]? ParseFrames(string aiResponse, int framePx = DefaultFramePx)
    {
        // 匹配 ```pixels ... ``` 代码块
        var match = Regex.Match(aiResponse, @"```pixels?\s*\n(.*?)```", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            // 回退：尝试匹配任意 ``` 代码块
            match = Regex.Match(aiResponse, @"```\s*\n(.*?)```", RegexOptions.Singleline);
            if (!match.Success) return null;
        }

        var block = match.Groups[1].Value;
        var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                          .Select(l => l.TrimEnd('\r'))
                          .Where(l => l.Length > 0 && l.All(c => !char.IsControl(c)))
                          .ToList();

        if (lines.Count == 0) return null;

        // 规范化：每行补/截到 framePx 长度，行数补/截到 framePx 行
        var frames = new List<string>();
        for (int i = 0; i < framePx; i++)
        {
            var line = i < lines.Count ? lines[i] : new string('.', framePx);
            if (line.Length < framePx)
                line = line.PadRight(framePx, '.');
            else if (line.Length > framePx)
                line = line.Substring(0, framePx);
            frames.Add(line);
        }
        return frames.ToArray();
    }

    /// <summary>用 AI 生成单帧像素画并渲染为 PNG。
    /// <param name="characterDescription">角色描述</param>
    /// <param name="palette">调色板</param>
    /// <param name="outputPath">输出 PNG 路径</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>生成是否成功 + 消息</returns>
    public static async Task<(bool Success, string Message)> GenerateFrameAsync(
        string characterDescription,
        Dictionary<char, Color> palette,
        string outputPath,
        CancellationToken ct = default)
    {
        var provider = AiProviderRegistry.Instance.Default;
        if (provider == null)
            return (false, "没有可用的 AI 提供方，请先在「⚙ 模型」中配置");

        var prompt = BuildPrompt(characterDescription, palette);
        Logger.Info("Generator", $"AI 生成像素画: 描述={characterDescription}");

        var request = new AiRequest
        {
            UserMessage = prompt,
            History = Array.Empty<ChatMessage>()
        };
        var resp = await provider.ChatAsync(request, ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(resp.Content))
            return (false, "AI 返回空内容");

        var frame = ParseFrames(resp.Content, DefaultFramePx);
        if (frame == null)
            return (false, "AI 回复中未找到字符网格代码块（```pixels```）");

        // 渲染为单帧 PNG（单帧 = 1×1 网格）
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        PixelArtRenderer.RenderSpriteSheetGrid(outputPath, new[] { frame }, palette, DefaultScale, cols: 1, rows: 1);
        Logger.Info("Generator", $"AI 像素画已生成: {outputPath}");

        return (true, $"已生成到 {outputPath}");
    }

    /// <summary>用 AI 生成多帧（4×4=16 帧）精灵图。
    /// 对每个表情分别生成一帧，组合成 sheet.png。
    /// 注意：需要多次 AI 调用，耗时较长。
    /// <param name="characterDescription">角色描述</param>
    /// <param name="palette">调色板</param>
    /// <param name="outputPath">输出 sheet.png 路径</param>
    /// <param name="ct">取消令牌</param>
    public static async Task<(bool Success, string Message)> GenerateSpriteSheetAsync(
        string characterDescription,
        Dictionary<char, Color> palette,
        string outputPath,
        CancellationToken ct = default)
    {
        var provider = AiProviderRegistry.Instance.Default;
        if (provider == null)
            return (false, "没有可用的 AI 提供方，请先在「⚙ 模型」中配置");

        var expressions = new[]
        {
            "睁眼（中性表情）",
            "闭眼（眨眼）",
            "微笑（开心）",
            "张嘴（惊讶/说话）",
            "撇眉（生气）",
            "半眯眼（困倦）",
            "大笑（兴奋）",
            "红脸（害羞）"
        };

        var frames = new List<string[]>();
        var prompt = BuildPrompt(characterDescription, palette);
        prompt += $"\n\n## 多帧生成\n请按顺序生成 {DefaultCols * DefaultRows} 帧，每帧用 ```pixels``` 代码块包裹。" +
                  $"帧顺序：{string.Join("、", expressions.Take(8))}，第 9-16 帧等同睁眼帧。";

        Logger.Info("Generator", $"AI 多帧生成开始: {DefaultCols * DefaultRows} 帧, 描述={characterDescription}");

        var request = new AiRequest
        {
            UserMessage = prompt,
            History = Array.Empty<ChatMessage>()
        };
        var resp = await provider.ChatAsync(request, ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(resp.Content))
            return (false, "AI 返回空内容");

        // 解析多个 ```pixels``` 代码块
        var matches = Regex.Matches(resp.Content, @"```pixels?\s*\n(.*?)```", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        foreach (Match m in matches)
        {
            var frame = ParseFrames(m.Groups[1].Value, DefaultFramePx);
            if (frame != null) frames.Add(frame);
        }

        // 不足 16 帧用第一帧补齐
        while (frames.Count < DefaultCols * DefaultRows)
        {
            frames.Add(frames.Count > 0 ? frames[0] : new string(Enumerable.Repeat('.', DefaultFramePx).ToArray())
                .Split().Select(_ => new string('.', DefaultFramePx)).ToArray());
        }

        if (frames.Count == 0)
            return (false, "AI 回复中未找到任何字符网格代码块");

        // 渲染
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        PixelArtRenderer.RenderSpriteSheetGrid(outputPath, frames.ToArray(), palette, DefaultScale, DefaultCols, DefaultRows);
        Logger.Info("Generator", $"AI 精灵图已生成: {outputPath}（{frames.Count} 帧）");

        return (true, $"已生成 {frames.Count} 帧到 {outputPath}");
    }
}
