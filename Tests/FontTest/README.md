# Font Test for CoyEngine

This test demonstrates bitmap font rendering using MonoGame.Extended.

## Quick Start

### Step 1: Generate a Font File

You need a BMFont format font file before running this test.

**Option A: Use an Online Generator (Easiest)**

1. Visit https://snowb.org/
2. Font size: 16
3. Click "Generate"
4. Download the ZIP
5. Extract and copy `font.fnt` and `font.png` to `Content/fonts/`
6. Rename to `test.fnt` and `test.png`

**Option B: Use Hiero (Recommended for quality)**

1. Download Hiero from https://github.com/libgdx/libgdx/wiki/Hiero
2. Run: `java -jar hiero.jar`
3. Select font: DejaVu Sans, size 16
4. Export format: BMFont (text)
5. Save to `Content/fonts/test.fnt` (the .png will be created alongside)

**Option C: Use BMFont (Windows only)**

1. Download from http://www.angelcode.com/products/bmfont/
2. Select font and size 16
3. Save as `test.fnt` in `Content/fonts/`

### Step 2: Run the Test

```bash
cd Tests/FontTest
dotnet run
```

You should see a window displaying:
- All uppercase and lowercase letters
- Digits 0-9
- Punctuation and symbols
- Sample sentences
- Multi-line text
- Scaled text examples

## File Structure

After generating a font, your structure should look like:

```
CoyEngine/
├── Tests/
│   └── FontTest/
│       ├── Program.cs
│       └── FontTest.csproj
└── Content/
    └── fonts/
        ├── test.fnt    <- BMFont data file
        └── test.png    <- Font texture
```

## What You'll See

The test displays:
- Full alphabet (A-Z, a-z)
- Numbers (0-9)
- Common punctuation and symbols
- Pangrams (sentences using all letters)
- Multi-line text support
- Text scaling (0.5x and 2.0x)
- FPS counter

## Troubleshooting

### "Could not find file 'Content/fonts/test.fnt'"
- Make sure you generated a font file and placed it in the correct directory
- The path is relative to where you run `dotnet run` from

### "Texture file not found"
- Ensure both `.fnt` and `.png` files are in the same directory
- The `.fnt` file references the `.png` by name - check they match

### Font looks pixelated
- This is normal for bitmap fonts at non-integer scales
- Try generating the font at a larger size for better quality

## Next Steps

After testing, use the same process to generate fonts for your game:
1. Choose your desired font family and size
2. Generate with Hiero or BMFont
3. Include in your game's Content folder
4. Load with `TinyBitmapFont.FromFile()`

See `FONTS.md` in the project root for complete documentation.
