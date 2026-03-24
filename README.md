# CoyEngine

A reusable 2D isometric game engine built with MonoGame.

## Features

- **Isometric Rendering**: Depth-buffered tile rendering with proper occlusion
- **TileRenderer**: Efficient terrain rendering with biome colors, slopes, and cliffs
- **UI Framework**: Complete screen-space UI system (Button, Label, Panel, etc.)
- **Camera System**: Smooth zoom, pan, and viewport management
- **World Generation**: Multi-scale noise, hydraulic erosion, water simulation
- **Input Handling**: Keyboard and mouse input mapping
- **Debug Tools**: Built-in visualization for heights, grids, and depth

## Installation

### Via NuGet

```bash
dotnet add package CoyEngine --version 0.1.0
```

### Local Development

1. Clone the repository
2. Build the project:
   ```bash
   dotnet build
   ```
3. Reference in your project or pack as NuGet package

## Quick Start

### Basic Setup

```csharp
using CoyEngine;
using CoyEngine.Core;
using CoyEngine.Rendering;
using CoyEngine.Rendering.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

public class MyGame : Game
{
    private GraphicsDeviceManager _graphics;
    private Camera _camera;
    private TileRenderer _tileRenderer;
    private GameMap _map;
    
    protected override void Initialize()
    {
        _graphics = new GraphicsDeviceManager(this);
        _camera = new Camera(_graphics.GraphicsDevice.Viewport);
        
        // Generate or load your map
        _map = GenerateMap();
        
        base.Initialize();
    }
    
    protected override void LoadContent()
    {
        var spriteBatch = new SpriteBatch(GraphicsDevice);
        var whitePixel = CreateWhitePixel();
        
        // Create tile renderer
        _tileRenderer = new TileRenderer(_map, whitePixel, _camera);
    }
    
    protected override void Update(GameTime gameTime)
    {
        // Update camera, input, etc.
        _camera.Move(new Vector2(1, 0));
        
        base.Update(gameTime);
    }
    
    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.CornflowerBlue);
        
        var context = new RenderContext(
            GraphicsDevice, 
            _spriteBatch, 
            _whitePixel, 
            null, 
            null
        );
        
        _tileRenderer.Draw(gameTime, context);
        
        base.Draw(gameTime);
    }
}
```

## Project Structure

```
CoyEngine/
├── Core/               # Core systems
│   ├── Camera.cs       # Viewport and transformation
│   ├── InputController.cs
│   ├── Settings.cs
│   ├── FPSCounter.cs
│   └── DebugSettings.cs
│
├── Rendering/          # Rendering system
│   ├── IRenderer.cs
│   ├── RenderContext.cs
│   └── World/
│       └── TileRenderer.cs
│
├── UI/                 # UI framework
│   ├── UIManager.cs
│   ├── UIComponent.cs
│   └── Components/
│       ├── Button.cs
│       ├── Label.cs
│       └── Panel.cs
│
├── World/              # World generation
│   ├── GameMap.cs
│   ├── Tile.cs
│   ├── WorldGenerator.cs
│   └── WaterSimulation.cs
│
└── Utilities/          # Helper utilities
    ├── MapEncoding.cs
    ├── CompressionUtils.cs
    └── ClientLogger.cs
```

## Versioning

This project uses [Semantic Versioning](https://semver.org/):

- **0.1.0** - Initial release with core rendering and UI
- **0.2.0** - [Planned] Entity rendering system
- **1.0.0** - [Planned] Stable API

### Pre-release Versions

Development versions use the format `X.Y.Z-dev.N`:

```xml
<PackageReference Include="CoyEngine" Version="0.2.0-dev.1" />
```

## Development

### Building

```bash
dotnet build
```

### Running Tests

```bash
dotnet test
```

### Creating NuGet Package

```bash
dotnet pack -c Release
```

## Usage in Games

See the [Birth_Of_Dog](https://github.com/yourusername/Birth_Of_Dog) project for a complete example of using CoyEngine in a game.

## License

MIT License - See LICENSE file for details.

## Acknowledgments

- Built with [MonoGame](https://www.monogame.net/)
- Originally extracted from Birth_Of_Dog
