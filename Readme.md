PopLottie - Lottie Animation Renderer for Unity UIToolkit
===================

Issues
----------
Please submit any problems with rendering, with a lottie file, and ideally, what it _should_ look like, with a ticket in github issues.

- Unity's ellipse renderer does not have 2D scaling, so is only scaled on the X.
	- `// todo: render this as a path when required`

Usage
-----------
- Add this package to Unity Packages
- Add A `LottieVisualElement` to a UIDocument
- Set the `Resource Filename` to a file within your `Assets/Resources/` folder. The filename needs to exclude the extension.
	- We're promised future versions of unity will allow asset-selection, but for now, we cannot implement custom inspector drawers, so filenames must be set manually.



Performance Notes
-------------
- Strokes are very expensive in unity's renderer! Having `EnableDebug=true`, which causes lots of control points of paths to be drawn, causes a large FPS hit!
