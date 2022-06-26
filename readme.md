# BadLuckBurt's skybox shader

This shader serves as a replacement for Daggerfall Unity's classic sky. It is an extended version of the shader from this tutorial [https://feralpug.github.io/OtherPages/Code/Pages/ExtendingUnitySkybox/ExtendingSkybox.html](https://feralpug.github.io/OtherPages/Code/Pages/ExtendingUnitySkybox/ExtendingSkybox.html "Feral Pug's Skybox tutorial") courtesy of Feral Pug who should get most of the credit in my opinion. This shader wouldn't exist without him. 

I suggest reading through the tutorial at least once to get a basic idea of how the shader works internally.

## Changes to the shader

I have added a second cloud layer, a second moon and changed the stars to use a diffuse texture and a black-and-white image mask to control the stars twinkle without affecting the surrounding sky.

# Setting up the shader in the editor
After you have cloned the shader repository and added it to the DFU project under **Assets/Game/Mods**, you will need to assign the skybox material and the directional Sunlight from the scene.

To do this, go to **Window > Rendering > Lighting Settings** in the menu bar, a new window will appear with global settings for the Unity renderer. You can dock this window if you like, just drag it next to the Inspector tab and Unity should dock it there.

Under **Environment** you will see two fields that are currently empty:

1. Skybox Material
2. Sun Source

In the Project Explorer, navigate to the skybox material, select and drag it into the Skybox Material field. 

Next, select the Sunlight in the scene hierarchy and drag it into the Sun Source field.

At this point you should see the skybox being rendered in the Scene view and are ready to go do the fun stuff.

## Import / export settings
In the menu bar you will see a new entry, **BLB**. If you click on that, a submenu will show with two options: 

1. Import skybox settings
2. Export skybox settings

You can use this to load and save particular skybox settings that you want to work on.

## The skybox material
To change the skybox settings, navigate to the BLBSkyboxMaterial in the Project Explorer and select the material. 

The exposed properties of the material will be shown in the Inspector, the scene will update automatically when you change settings.

# Material settings

**Retro**

* LUT Texture - ignore for now
* Reduce color - WIP: Enables very basic color reduction

**SkyAndSun**

* **Sun** - sun quality settings, anything lower than High quality doesn't look good :D
* **Sun Size** - determines the sun size in the sky
* **Sun Size Convergence** - determines how big the sun's corona is
* **Atmosphere thickness** - determines the amount of light scattering, values below 1 tend to look nicer imo.
* **Sky tint** - slightly influences the sky color together with Atmospheric thickness
* **Ground** - the color rendered below the horizon
* **Exposure** - Determines the light exposure of the skybox
* **Night start height | Night end height** - Determines how high in the sky the stars start to be drawn
* **Sky Fade Start | Sky End Start** - Determines how high in the sky the clouds / moons start to be drawn
* **Fog distance** - used in calculating how much the weather fog will influence the skybox. DFU uses Exponential fog by default and finding the right distance is a bit trial and error because of that. The fog distance in the editor needs to be multiplied by 1.5 or 2 to get the proper distance.

**CloudsTop and Clouds**

The settings for these sections do the same thing, just on a separate texture which gives us two cloud layers. 

To understand the alpha stuff: the shader samples a pixel from the cloud texture, the value read ranges from 0.0 to 1.0, 0 being transparent and 1 being fully opaque.

* **Diffuse** - Takes a grayscale texture that determines the cloud shapes.
* **Tiling and offset** - Tiling can be used to stretch or compress the texture, I recommend stretching a bit to prevent visible tiling of the texture. Offset is used to move the UV coordinates around, mostly useful when you use the same texture for both cloud layers.
* **Normal** - The normal map used to shade / color the clouds to make them less flat. I typically use the cloud texture itself and have Unity downsample it to a lower resolution which helps soften the normal effect. Take a look at the normal map's texture settings in the Inspector by selecting it in the Project Explorer.
* **Color and Night color** - The color used to tint / shade the clouds. There is a separate night color because the day color is usually too bright at night.
* **Alpha threshold** - This setting makes harder edges because of the stricter cut-off that happens. If this value is greater than Alpha max, it essentially 'inverts' the cloud rendering (do not recommend but it could work)
* **Alpha max** - This setting determines how much of the cloud texture is visible (anything lower than the max value will be transparent, anything above it will grow more opaque the closer it gets to 1.0 as explained earlier
* **Color boost** - broken on the top layer for some reason but it can be used to intensify the color that the cloud gets, I don't use this personally so it either needs to be removed or I need to fix that top layer boost
* **Normal effect** - determines how heavy the normal effect is, it works in conjunction with the value you set on the normal map texture itself.
* **Opacity** - This determines the global opacity for the cloud layer. I made the layers slightly transparent to allow the sun disc to show through the thicker clouds if their combined opacity is less than 1.0
* **Bending** - Changes the perspective of the clouds rendering, 0 is a completely flat plane, 1.0 is warped to a full circle. I personally like a slight bend.
* **Normal speed** - Haven't used this much but it was originally intended to give the impression of 'changing' clouds when using one layer. Since I added the second layer, this may have become an unneccessary setting.
* **Cloud speed** - Determines how fast the clouds move across the skybox
* **Cloud direction** - 0-360 degrees wind direction

I may remove the following properties as I'm not convinced they still serve a purpose:

* **Cloud blend speed** - Determines the offset when the shader samples a cloud texture for the second time and blends it with the first sample
* **Cloud blend scale** - Modifier for the blend values
* **Cloud Blend LB / UB** - Values for the lower bound and upperbound 
