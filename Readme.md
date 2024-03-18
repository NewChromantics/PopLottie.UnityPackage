PopLottie - Lottie Animation Renderer for Unity UIToolkit
===================

Issues
----------
Please submit any problems with rendering, with a lottie file, and ideally, what it _should_ look like, with a ticket in github issues.

- Unity's ellipse renderer does not have 2D scaling, so is only scaled on the X.
	- `// todo: render this as a path when required`

Usage
-----------
- Add this package to Unity Packages with _Add Package from Git URL:_ [https://github.com/NewChromantics/PopLottie.UnityPackage.git](https://github.com/NewChromantics/PopLottie.UnityPackage.git)
- Add A `LottieVisualElement` to a UIDocument
- Set the `Resource Filename` to a file within your `Assets/Resources/` folder. The filename needs to exclude the extension.
	- We're promised future versions of unity will allow asset-selection, but for now, we cannot implement custom inspector drawers, so filenames must be set manually.

Future Plans
----------
Contributions for these are all welcome.
- Split BodyMovin parser from the renderer.
	- Renderer create painter2d-render-commands for user (ie. the element) to cache at higher level.
	- Options to output variables/uniforms for animated parts, to allow future use in shaders, or manual interpolation in render-command-cache
	- Options to output Editor/handle-control references back to layers & shapes to ease in-unity-editor editing
- Correct timing for user to control 0th frame time.
- `async` functions to play & wait for animations
- Looping controls
- Preview render of assets
- Swift, javascript
- Shader renderer for use in texturing, projection, world space use.
- Optimisation!

Performance Notes
-------------
- Strokes are very expensive in unity's renderer! Having `EnableDebug=true`, which causes lots of control points of paths to be drawn, causes a large FPS hit!
