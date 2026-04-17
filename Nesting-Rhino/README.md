# Nesting Rhino Plugin

This folder contains the Rhino nesting plugin and its source code.

## Install In Rhino

1. Open Rhino.
2. Run the Rhino command `PluginManager`.
3. Click `Install`.
4. Select the plugin file from this folder:

   ```text
   dist\NestingRhino.rhp
   ```

   For a specific Rhino version, use one of these files instead:

   ```text
   dist\NestingRhino-Rhino8.rhp
   dist\NestingRhino-Rhino7.rhp
   ```

5. Restart Rhino if Rhino asks you to.
6. Run the command:

   ```text
   NestingRhino
   ```

## Build The Plugin

From PowerShell, run this command inside the `Nesting-Rhino` folder:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -RhinoVersion 8
```

For Rhino 7, use:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -RhinoVersion 7
```

The build creates the plugin files in the `dist` folder.

## If Rhino Blocks The Plugin

If Windows downloaded or copied the plugin from another machine, Rhino may refuse to load it.

1. Right-click the `.rhp` file.
2. Choose `Properties`.
3. If there is an `Unblock` checkbox, enable it.
4. Click `Apply`.
5. Install the plugin again from Rhino's `PluginManager`.

## Files

- `src\NestingRhinoScript.cs`: standalone Rhino C# script version.
- `plugin\`: compiled plugin source files.
- `dist\`: compiled `.rhp` plugin files.
- `build.ps1`: build script for Rhino 7 or Rhino 8.
