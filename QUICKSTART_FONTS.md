# CoyEngine Font Quick Start

## TL;DR - Get Fonts Working in 3 Steps

### 1️⃣ Generate a Font (2 minutes)

Go to **https://snowb.org/** and:
- Font size: `16`
- Click **"Generate"**
- Download the ZIP
- Extract `font.fnt` and `font.png`

### 2️⃣ Add to Your Project

```bash
# Create fonts directory
mkdir -p Content/Fonts

# Copy your generated files
cp ~/Downloads/font.fnt Content/Fonts/myfont.fnt
cp ~/Downloads/font.png Content/Fonts/myfont.png
```

### 3️⃣ Use in Code

```csharp
using CoyEngine.Core;

// Load the font
var font = TinyBitmapFont.FromFile(GraphicsDevice, "Content/Fonts/myfont.fnt");

// Draw text
spriteBatch.Begin();
font.DrawString(spriteBatch, "Hello Linux!", new Vector2(100, 100), Color.White);
spriteBatch.End();
```

## That's It! 🎉

Your game now has cross-platform font rendering that works on Linux, Windows, and macOS.

---

## For More Details

- **`FONTS.md`** - Complete documentation
- **`FONT_SOLUTION.md`** - Technical summary
- **`Tests/FontTest/README.md`** - Test instructions

## Font Tools

| Tool | Best For | URL |
|------|----------|-----|
| **snowb.org** | Quick testing | https://snowb.org/ |
| **Hiero** | Production quality | https://github.com/libgdx/libgdx/wiki/Hiero |
| **BMFont** | Windows users | http://www.angelcode.com/products/bmfont/ |
