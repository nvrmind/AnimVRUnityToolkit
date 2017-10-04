# AnimVR Unity Toolkit
This packages allows you to drag the .stage files you create with [AnimVR](https://nvrmind.io/#animvr) into [Unity3D](https://unity3d.com) and treat them like any other asset.

## Main Features

- Import complete stages, including animation, meshes, textures, cameras and sound.
- Use the Unity Timeline API to playback animations, do scene transitions and integrate it with the rest of your game/movie.
- Create new stages directly in the Unity Editor and open them in AnimVR. When you save, the changes are instantly visible in Unity.

## Getting Started

- Download the Unity Package [here]()
- Import it into your Unity project as described [here](https://docs.unity3d.com/Manual/AssetPackages.html)
- Right click in your Unity project panel and choose "Create -> AnimVR -> Stage" (you can also drag an exisiting stage into your project window).
- Double click the created asset. The first time you do this you'll need to set up the application to open .stage files with. In Windows 10 this done by clicking "More Apps", scrolling all the way down and clicking "Look for another app on this PC". Look for the folder your copy of AnimVR is installed to and choose "ANIMVR.exe".
- AnimVR will open and load the stage. Make any changes you want and save the file as usual. When you go back to Unity the asset will get reimported.
- You can drag the imported asset into a stage like any other mesh or prefab. As long as the prefab instance is intact, changes to the stage will be reflected everywhere you are using the prefab.

## Things to watch out for
- Currently we only support import on Windows platforms! We plan on supporting MacOS in the future and would love to hear from you if you urgently need MacOS support. Note that this *only* concerns importing stage files. You can still use the imported file on MacOS.
- Stages with audio need to be imported twice the first time you import them. (Just right click the stage and press "Reimport").

### If you run into any problems, please open an issue in this repository!
