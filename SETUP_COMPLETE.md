# CoyEngine Setup Complete! ✅

## What's Been Done

### 1. ✅ Separate Git Repository Created

**Location:** `/home/caine/Development/CoyEngine`

```
CoyEngine/
├── .git/              # Fresh git history
├── .gitignore
├── LICENSE            # MIT License
├── README.md
├── CoyEngine.csproj   # Version 0.1.0
├── Core/
├── Rendering/
├── UI/
├── World/
├── Utilities/
└── Network/
```

**Git Status:**
- ✅ Initial commit: "CoyEngine v0.1.0"
- ✅ Second commit: "Add MIT License"
- ✅ Clean history (2 commits)
- ✅ Ready to push to GitHub/GitLab

---

### 2. ✅ First NuGet Package Created

**Package:** `CoyEngine.0.1.0.nupkg`  
**Location:** `/home/caine/Development/Birth_Of_Dog/src/nuget-packages/`

**NuGet.config** created in Birth_Of_Dog root pointing to local feed.

---

### 3. ✅ Current Setup

Birth_Of_Dog currently uses **project reference**:
```xml
<ProjectReference Include="..\CoyEngine\CoyEngine.csproj" />
```

This is **perfect for development** - you get:
- ✅ Live updates when you change engine code
- ✅ No need to rebuild package constantly
- ✅ Easy debugging

---

## Next Steps

### Option A: Continue with Project Reference (Recommended for Now)

Keep the current setup while actively developing both projects.

**When to switch:** When you want to test the NuGet package workflow or release stable versions.

---

### Option B: Switch to NuGet Package Reference

When ready to use the published package:

1. **Edit** `src/Birth_Of_Dog.Client/Birth_Of_Dog.Client.csproj`:

```xml
<!-- Remove this line -->
<ProjectReference Include="..\CoyEngine\CoyEngine.csproj" />

<!-- Add this line -->
<PackageReference Include="CoyEngine" Version="0.1.0" />
```

2. **Restore and build:**
```bash
cd /home/caine/Development/Birth_Of_Dog
dotnet restore
dotnet build
```

---

### Option C: Push to GitHub (For Remote Access)

1. **Create GitHub repository:**
   - Go to https://github.com/new
   - Repository name: `CoyEngine`
   - Make it Public or Private (your choice)
   - Don't initialize with README (we already have one)

2. **Push your code:**
```bash
cd /home/caine/Development/CoyEngine
git remote add origin https://github.com/YOUR_USERNAME/CoyEngine.git
git branch -M main
git push -u origin main
```

3. **Enable GitHub Packages (Optional):**
   - In your GitHub repo, go to Settings → Packages
   - Follow instructions to publish NuGet packages to GitHub Packages

---

## Development Workflow

### Parallel Development (Current Setup)

```
┌──────────────────────┐         ┌──────────────────────┐
│   CoyEngine Repo     │         │  Birth_Of_Dog Repo   │
│   v0.1.0 (stable)    │         │  Uses: Project Ref   │
│                      │         │                      │
│   Develop v0.2.0-dev │         │  Gets updates        │
│   ───────────────────┼────────►│  automatically       │
└──────────────────────┘         └──────────────────────┘
```

### When to Create New Version

**Create v0.2.0 when:**
- ✅ You've added significant new features
- ✅ API changes are stable
- ✅ You want to "lock in" a version for the game

**Process:**
1. Update version in `CoyEngine.csproj`:
   ```xml
   <Version>0.2.0</Version>
   ```

2. Commit and tag:
   ```bash
   cd /home/caine/Development/CoyEngine
   git add .
   git commit -m "Release v0.2.0"
   git tag v0.2.0
   git push origin main --tags
   ```

3. Build package:
   ```bash
   dotnet pack -c Release
   ```

4. Update Birth_Of_Dog to use new version

---

## Quick Commands

### Build CoyEngine
```bash
cd /home/caine/Development/CoyEngine
dotnet build
```

### Create NuGet Package
```bash
cd /home/caine/Development/CoyEngine
dotnet pack -c Release
# Package appears in: bin/Release/CoyEngine.0.2.0.nupkg
```

### Update Birth_Of_Dog
```bash
cd /home/caine/Development/Birth_Of_Dog
dotnet restore
dotnet build
```

### Run Tests
```bash
cd /home/caine/Development/Birth_Of_Dog
dotnet test
```

---

## Version History

| Version | Date | Status | Notes |
|---------|------|--------|-------|
| 0.1.0 | Mar 24, 2025 | ✅ Released | Initial release from Birth_Of_Dog |
| 0.2.0 | TBD | 🔄 Planned | Next version |

---

## File Locations

```
/home/caine/Development/
├── CoyEngine/                          # ← Separate git repo
│   ├── .git/
│   ├── CoyEngine.csproj                # Version: 0.1.0
│   └── ...
│
├── Birth_Of_Dog/                       # ← Game repo
│   ├── NuGet.config                    # Points to local feed
│   ├── src/
│   │   ├── CoyEngine/                  # ← Still here for project ref
│   │   ├── Birth_Of_Dog.Client/        # Uses CoyEngine
│   │   └── nuget-packages/             # Local NuGet feed
│   │       └── CoyEngine.0.1.0.nupkg
│   └── ...
│
└── nuget-packages/                     # Future: global local feed
```

---

## Troubleshooting

### Package Not Found
```bash
# Clear NuGet cache
dotnet nuget locals all --clear

# Force restore
dotnet restore --force
```

### Build Fails After Switch
```bash
# Clean everything
dotnet clean
rm -rf bin obj
dotnet restore
dotnet build
```

---

## Summary

✅ **CoyEngine** is now a standalone repository  
✅ **Version 0.1.0** is tagged and packaged  
✅ **Birth_Of_Dog** can use it via project reference (current) or NuGet package  
✅ **Local NuGet feed** is configured and working  
✅ **Ready for parallel development**  

You can now:
- Develop CoyEngine independently
- Version it separately
- Use it in multiple projects
- Publish to NuGet when ready

🎉 **Congratulations!** Your engine extraction is complete!
