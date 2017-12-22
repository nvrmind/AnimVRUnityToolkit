[![header](https://github.com/nvrmind/AnimVRUnityToolkit/raw/master/img/citypicture.PNG)](https://vimeo.com/238229098)

# AnimVR Unity Toolkit
This packages allows you to drag the .stage files you create with [AnimVR](https://nvrmind.io/#animvr) into [Unity3D](https://unity3d.com) and treat them like any other asset.

## Main Features

- Import complete stages, including animation, meshes, textures, cameras and sound.
- Use the Unity Timeline API to playback animations, do scene transitions and integrate it with the rest of your game/movie.
- Create new stages directly in the Unity Editor and open them in AnimVR. When you save, the changes are instantly visible in Unity.

## Requirements
- Unity 2017.1
- AnimVR beta

## Getting Started

- Download the Unity Package [here](https://github.com/nvrmind/AnimVRUnityToolkit/releases/download/v8.11.1_beta/AnimVR.Unity.Toolkit.unitypackage)
- Import it into your Unity project as described [here](https://docs.unity3d.com/Manual/AssetPackages.html)
- Right click in your Unity project panel and choose "Create -> AnimVR -> Stage" (you can also drag an exisiting stage into your project window).
- Double click the created asset. The first time you do this you'll need to set up the application to open .stage files with. In Windows 10 this done by clicking "More Apps", scrolling all the way down and clicking "Look for another app on this PC". Look for the folder your copy of AnimVR is installed to and choose "ANIMVR.exe".
- AnimVR will open and load the stage. Make any changes you want and save the file as usual. When you go back to Unity the asset will get reimported.
- You can drag the imported asset into a stage like any other mesh or prefab. As long as the prefab instance is intact, changes to the stage will be reflected everywhere you are using the prefab.

## Things to watch out for
- Currently we only support import on Windows platforms! We plan on supporting MacOS in the future and would love to hear from you if you urgently need MacOS support. Note that this *only* concerns importing stage files. You can still use the imported file on MacOS.
- Stages with audio need to be imported twice the first time you import them. The import editor will show a "Fixup audio clips" button if that's the case. Just press it and you're good to go!

### If you run into any problems, please open an issue in this repository!

Dario wrote a blog post on some of the technical considerations and implementation details on [his website](https://darioseyb.com/post/unity-importer/).

____
## Documentation
### Import Settings
#### Base Shader

Change this to set the Shader to use for all imported objects. The shader used should support vertex colors. Additionally, following properties are set by the importer: 
 - \_Color:         _The base diffuse color._
 - \_SpecColor:     _The base specular color._
 - \_EmissionColor: _The base emission color._
 - \_Unlit:         _Float that indicates if the mesh should be affected by lighting._
 - \_Gamma:         _Gamma value, 1.0 when linear color space is selected in AnimVR, 2.2 otherwise._
 
 #### Default Wrap Mode
 
 The wrap mode of the created `PlayableDirector`.
 
 #### Audio Import Setting
 
 Set how to handle audio layers.
 
 - None: Don't import anything
 - Only Clips: Import sound files and create Unity audio clips
 - Clips and Tracks: Import files and also create timeline audio tracks (experimental)
 
 #### Import Cameras
 
 Whether or not to import camera layers.

