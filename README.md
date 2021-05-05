# HaloSpeedrunTools

## Description
This application is meant to deliver multiple useful quality-of-life tools for Halo speedrunners. See the "Features" list below for a list of all current and planned features. The application only currently supports Halo: The Master Chief Collection on PC, however may be expanded as Halo: Infinite (and/or other ports) release in the future.
**Steam and Windows Store are both supported.**

## Features
### Current Features
- Automatic Recording of ILs: When enabled, the program will start/stop OBS recording through websocket when you start/stop an IL. Videos output to `<OBS output dir>/HST`, with the *option* of saving incomplete ILs.
- Built-in obs-websocket installer.

### Planned Features
- Inclusive and easy to access API to access commonly used values in MCC's memory.
- Race framework to draw markers on screen for player locations.
- Automatic HR.com submission
- Automatic YouTube uploads
- Stream Assets such as Speedometer

## Dependencies
*All dependencies listed here are included in the project itself*.
- [.NET Framework 4.7.2](https://dotnet.microsoft.com/download/dotnet-framework/net472)
- [obs-websocket](https://github.com/Palakis/obs-websocket/releases/tag/4.9.0)

## Installation and Usage
### Installation
1. Download the latest release from the [Releases page](https://github.com/x-e-r-o/HaloSpeedrunTools/releases).
2. Extract the zip file into any suitable location.
3. Run the program as Administrator (will not work otherwise).
4. Launch MCC with anti-cheat OFF to hook process. 

### Usage
Currently, the only feature is the IL recording. To use this, ensure you first have obs-websocket installed, or this will not work. If you don't have obs-websocket already, simply:
1. Press the `Install OBS Websocket` button in the application
2. Select OBS installation directory
3. Press `Install OBS Websocket`

After doing this, close and re-open OBS (if it was already open), and go to `Tools > WebSockets Server Settings`. Ensure your settings match this:

![Screenshot](https://i.imgur.com/ewbrQ3C.png)

**It is advisable that you use a password.** Once you have set this up, go back to the application and put the password (leave blank if none) in the textbox above the `Install OBS Websocket` button. You may then click `Connect to OBS`.

To enable the recording feature, tick the corresponding checkbox and it will automatically work.

## Notes
- Repeatedly Restarting Mission may cause overlap on recordings.
- Please report any issues to Xero, but keep in mind that this is very early in progress.

Credit to [Burnt](https://github.com/Burnt-o) for memory addresses and a [stub](https://github.com/Burnt-o/StubStuff) upon which this was built.
