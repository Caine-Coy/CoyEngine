# Cross-Platform Font Rendering with MonoGame.Extended

This guide explains how to use bitmap fonts in CoyEngine using MonoGame.Extended.

## Overview

CoyEngine uses **MonoGame.Extended** for bitmap font rendering, which provides:
- ✅ Full cross-platform support (Windows, Linux, macOS)
- ✅ BMFont format support (.fnt + .png)
- ✅ High-quality rendered fonts
- ✅ No runtime font generation needed

## Quick Start

### 1. Generate a Font Atlas

Use one of these tools to create BMFont format fonts:

#### **Option A: Hiero (Cross-platform, Recommended)**
Download: https://github.com/libgdx/libgdx/wiki/Hiero

1. Open Hiero (Java-based, works on all platforms)
2. Choose your font family and size
3. Set export format to **BMFont (text)**
4. Export the `.fnt` file and `.png` texture

#### **Option B: BMFont (Windows only)**
Download: http://www.angelcode.com/products/bmfont/

1. Select font and size
2. Export as BMFont format
3. Copy `.fnt` and `.png` to your content folder

#### **Option C: Online Generator**
Visit: https://snowb.org/ or https://bmfont.vercel.app/

1. Configure font settings
2. Download the generated files

### 2. Add to Your Project

Place the `.fnt` and `.png` files in your content directory:
```
Content/
└── Fonts/
    ├── myfont.fnt
    └── myfont.png
```

### 3. Load and Use

```csharp
using CoyEngine.Core;

// In LoadContent()
var font = TinyBitmapFont.FromFile(GraphicsDevice, "Content/Fonts/myfont.fnt");

// In Draw()
spriteBatch.Begin();
font.DrawString(spriteBatch, "Hello World!", new Vector2(100, 100), Color.White);
spriteBatch.End();
```

## API Reference

### TinyBitmapFont

```csharp
// Load from file
var font = TinyBitmapFont.FromFile(GraphicsDevice, "Content/Fonts/myfont.fnt");

// Load from stream
using (var stream = File.OpenRead("Content/Fonts/myfont.fnt"))
{
    var font = TinyBitmapFont.FromStream(GraphicsDevice, stream);
}

// Draw text
font.DrawString(spriteBatch, "Hello", new Vector2(100, 100), Color.White);

// Draw with scale
font.DrawString(spriteBatch, "Big Text", new Vector2(100, 150), Color.White, 2.0f);

// Draw with rotation
font.DrawString(spriteBatch, "Rotated", new Vector2(100, 200), Color.White, 
    MathHelper.ToRadians(45), Vector2.Zero);

// Measure text
var size = font.MeasureString("Hello World");
float width = size.X;
float height = size.Y;

// Get line height
int lineHeight = font.LineHeight;
```

## Recommended Settings for Hiero

- **Font**: DejaVu Sans, Arial, or any system font
- **Size**: 14-18 for UI, 20-24 for headings
- **Characters**: Default ASCII (32-127) is usually sufficient
- **Export Format**: BMFont (text)
- **Texture Format**: PNG (RGBA8888)
- **Scale**: 1.0
- **Padding**: 2-4 pixels

## Example: Complete Game Setup

```csharp
public class Game1 : Game
{
    private GraphicsDeviceManager _graphics;
    private SpriteBatch _spriteBatch;
    private TinyBitmapFont _font;
    
    public Game1()
    {
        _graphics = new GraphicsDeviceManager(this);
        Content.RootDirectory = "Content";
    }
    
    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        
        // Load font (make sure myfont.fnt and myfont.png exist)
        _font = TinyBitmapFont.FromFile(GraphicsDevice, "Content/Fonts/myfont.fnt");
    }
    
    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.CornflowerBlue);
        
        _spriteBatch.Begin();
        
        _font.DrawString(_spriteBatch, "Hello Cross-Platform!", new Vector2(50, 50), Color.White);
        _font.DrawString(_spriteBatch, $"FPS: {1f / gameTime.ElapsedGameTime.TotalSeconds:F0}", 
            new Vector2(50, 80), Color.Yellow);
        
        // Multi-line text
        _font.DrawString(_spriteBatch, "Line 1\nLine 2\nLine 3", 
            new Vector2(50, 120), Color.LightGreen);
        
        // Scaled text
        _font.DrawString(_spriteBatch, "Big Text", new Vector2(50, 200), Color.Orange, 2.0f);
        
        _spriteBatch.End();
        
        base.Draw(gameTime);
    }
}
```

## Font Generation Tools Comparison

| Tool | Platform | Quality | Ease |
|------|----------|---------|------|
| **Hiero** | All (Java) | High | Easy |
| **BMFont** | Windows | High | Easy |
| **snowb.org** | All (Web) | Medium | Very Easy |
| **bmfont.vercel.app** | All (Web) | Medium | Very Easy |

## Troubleshooting

### Font not loading
- Ensure both `.fnt` and `.png` files are in the same directory
- Check that the `.fnt` file references the correct `.png` filename
- The .png file must be in the same directory as the .fnt file when loading

### Text looks blurry
- Make sure you're using `SamplerState.PointClamp` or appropriate sampler state
- Check that the font texture isn't being downscaled

### Missing characters
- Regenerate the font with a wider character set
- BMFont format supports extended ASCII and Unicode

## Free Font Resources

- **Google Fonts**: https://fonts.google.com/ (download TTF, convert with Hiero)
- **DaFont**: https://www.dafont.com/ (free fonts, check licenses)
- **Font Squirrel**: https://www.fontsquirrel.com/ (commercial-free fonts)

## Migration Notes

If you were using the old procedural font system, the API is nearly identical:

```csharp
// OLD (procedural)
var font = TinyBitmapFont.CreateProcedural(GraphicsDevice, 16);

// NEW (MonoGame.Extended with BMFont)
var font = TinyBitmapFont.FromFile(GraphicsDevice, "Content/Fonts/myfont.fnt");
```

Just generate your fonts ahead of time and the usage is the same!

## License

MonoGame.Extended is licensed under MIT. This font system is part of CoyEngine and is also MIT licensed.
