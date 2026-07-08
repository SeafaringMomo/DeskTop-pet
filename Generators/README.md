# Generators 像素画与背景音乐生成器

本目录独立存放**程序化生成角色资源**的代码，与主应用逻辑解耦。包含像素画精灵图生成器与背景音乐生成器，应用启动时自动调用，为每个角色生成默认的 `sheet.png`、`icon.png`、`bgm.wav`。用户也可手动替换这些文件，已存在时生成器会跳过。

## 文件清单

| 文件 | 作用 | 输出 |
|---|---|---|
| `PixelArtRenderer.cs` | 通用像素画渲染器（字符网格 + 调色板 → PNG） | `sheet.png` + `icon.png` |
| `LuffySpriteGenerator.cs` | 路飞 32×32 像素画生成器 | `static/luffy/sheet.png` + `icon.png` |
| `ZoroSpriteGenerator.cs` | 索隆 32×32 像素画生成器 | `static/zoro/sheet.png` + `icon.png` |
| `BgmGenerator.cs` | 程序化背景音乐生成器（NAudio 波形合成） | `static/zoro/bgm.wav` |

## 命名空间

`DesktopPetCSharp.Generators`（与 `Services` 分离，体现独立性）

## 像素画生成原理

### 字符网格 + 调色板

每个角色用"字符网格"描述一帧像素画，每个字符代表一种颜色：

```csharp
private static readonly string[] FrameOpen =
{
    "................................",  // '.' = 透明
    "..........TTTTTT..............",   // 'T' = 草帽麦秆黄
    ".........TTTTTTTT.............",
    "........TTTTTTTTTT............",
    // ...
};
```

调色板是 `Dictionary<char, Color>`，把字符映射到 ARGB 颜色：

```csharp
private static readonly Dictionary<char, Color> Palette = new()
{
    ['.'] = Color.FromArgb(0, 0, 0, 0),      // 透明
    ['T'] = Color.FromArgb(255, 232, 197, 71), // 草帽麦秆黄
    ['R'] = Color.FromArgb(255, 192, 57, 43),  // 帽带红
    // ...
};
```

### 渲染流程

`PixelArtRenderer.RenderSpriteSheet` 完成：

1. **精灵图（sheet.png）**：多帧横向排列。第 0 帧占左半，第 1 帧占右半。每帧 32×32 像素，4 倍放大 → 单帧 128×128，整图 256×128。
2. **图标（icon.png）**：取第 0 帧单独输出为正方形图标，用于系统托盘。

渲染使用 `WriteableBitmap` 直接写像素字节（BGRA），再用 `PngBitmapEncoder` 保存为 PNG。透明像素 alpha=0，保证 WPF 透明窗口的逐像素点击穿透能正常工作。

### 多帧动画

精灵图采用横向排列多帧：
- 第 0 帧：睁眼（默认显示，持续 `blink_interval_ms` 毫秒，默认 3000ms）
- 第 1 帧：眨眼（短暂显示 150ms 后切回）

`PetWindow` 用 `CroppedBitmap` 按列数切分精灵图，按节律切换帧。

## 背景音乐生成原理

`BgmGenerator` 用 NAudio 的 `WaveFileWriter` 合成 WAV 音频：

1. **音符序列**：定义一段简短旋律（频率 + 时长），如索隆的武士风小调：E4 → G4 → A4 → B4 → A4 → G4 → E4（长音）→ 静音 → D4 → E4 → G4（长音）→ 静音
2. **波形合成**：每个音符用正弦波 + 二次谐波合成，模拟器乐音色：
   ```csharp
   sample = 0.4 * Math.Sin(2 * Math.PI * freq * t)
          + 0.15 * Math.Sin(2 * Math.PI * freq * 2 * t);
   ```
3. **淡入淡出**：前 50ms 淡入、后 80ms 淡出，避免爆音
4. **循环 3 次**：把旋律重复 3 次凑够长度，配合 `LoopStream` 实现无缝循环
5. **格式**：22050Hz 单声道 16bit PCM WAV

索隆默认用此生成器；路飞不生成（等待用户放入 `bgm.mp3`）。

## 如何新增角色生成器

以新增"娜美"为例：

### 1. 创建生成器类

新建 `NamiSpriteGenerator.cs`：

```csharp
using System.Windows.Media;

namespace DesktopPetCSharp.Generators;

public static class NamiSpriteGenerator
{
    public const int FramePx = 32;
    public const int Scale = 4;

    private static readonly Dictionary<char, Color> Palette = new()
    {
        ['.'] = Color.FromArgb(0, 0, 0, 0),
        ['O'] = Color.FromArgb(255, 255, 165, 0),   // 橘色头发
        ['S'] = Color.FromArgb(255, 244, 194, 161),  // 肤色
        // ... 更多颜色
    };

    private static readonly string[] FrameOpen = { /* 32×32 字符网格 */ };
    private static readonly string[] FrameBlink = { /* 32×32 字符网格 */ };

    public static void EnsureGenerated(string sheetPath)
    {
        var frames = new[] { FrameOpen, FrameBlink };
        PixelArtRenderer.RenderSpriteSheet(sheetPath, frames, Palette, Scale);
    }
}
```

### 2. 注册到 SpriteGeneratorRegistry

在 `App.xaml.cs` 的 `SpriteGeneratorRegistry.EnsureGenerated` 中添加 case：

```csharp
switch (dirName.ToLowerInvariant())
{
    case "luffy": LuffySpriteGenerator.EnsureGenerated(sheetPath); break;
    case "zoro":  ZoroSpriteGenerator.EnsureGenerated(sheetPath); break;
    case "nami":  NamiSpriteGenerator.EnsureGenerated(sheetPath); break;
}
```

### 3. 添加角色配置

在 `config/config_pet.yaml` 添加：

```yaml
roles:
  路飞: luffy
  索隆: zoro
  娜美: nami        # 新增

themes:
  娜美:              # 可选：配置配色
    primary: "#FF8C00"
    accent: "#FFD700"
    # ...

frame_scale:
  娜美:
  - 150
  - 0.4
```

### 4. 启动验证

运行应用，检查 `static/nami/` 下是否生成了 `sheet.png` 与 `icon.png`。托盘「人物」子菜单应出现「娜美」选项。

## 如何为角色生成默认 BGM

在 `SpriteGeneratorRegistry.EnsureDefaultBgm` 中添加 case：

```csharp
switch (dirName.ToLowerInvariant())
{
    case "zoro": BgmGenerator.EnsureGenerated(wav); break;
    case "nami":
        // 可选：为娜美生成不同的旋律
        BgmGenerator.EnsureGenerated(wav);
        break;
}
```

如需自定义旋律，可修改 `BgmGenerator` 的 `notes` 数组，或新建一个独立的生成器类。

## 独立使用（脱离主应用）

这些生成器是纯静态类，只依赖 `System.Windows.Media` + `NAudio`，可独立调用：

```csharp
using DesktopPetCSharp.Generators;

// 生成路飞精灵图到指定路径
LuffySpriteGenerator.EnsureGenerated(@"D:\output\luffy\sheet.png");

// 生成索隆精灵图
ZoroSpriteGenerator.EnsureGenerated(@"D:\output\zoro\sheet.png");

// 生成默认 BGM
BgmGenerator.EnsureGenerated(@"D:\output\zoro\bgm.wav");

// 用自定义网格 + 调色板生成精灵图
var palette = new Dictionary<char, Color>
{
    ['.'] = Color.FromArgb(0, 0, 0, 0),
    ['R'] = Color.FromArgb(255, 255, 0, 0),
};
var frames = new[] { new[] { "RR..", ".RR.", "..RR" }, new[] { ".RR.", "RR..", ".RR." } };
PixelArtRenderer.RenderSpriteSheet(@"D:\output\custom.png", frames, palette, scale: 8);
```

## 设计说明

- **静态类 + EnsureGenerated 模式**：所有生成器都是 `static class`，方法名统一为 `EnsureGenerated(path)`，文件已存在则跳过，便于用户手动替换。
- **32×32 像素 + 4 倍放大**：比 16×16 更精细，4 倍放大输出 128×128，兼顾像素感与辨识度。
- **透明背景**：使用 `'.'` 字符表示透明（alpha=0），保证 WPF 透明窗口的逐像素点击穿透。
- **NAudio 波形合成**：不依赖外部音频文件，用数学函数生成波形，可独立运行。
