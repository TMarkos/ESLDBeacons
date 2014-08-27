ESLD Jump Beacons
==========

With projects like MKS/OKS and Fine Print bases/stations in far-flung reaches of the solar system, it can drag a bit to have to do the whole wait/transfer/wait/insert dance over and over again.  In an effort to keep the KSP endgame balanced and fun, I've cobbled together some jump beacons.

These are not cheaty, easy jump beacons, though.  They incorporate:
* Line of sight checking.
* Gravity restrictions (also checked under LoS).
* Incredibly costly fuel.  More on that in a second.
* Scaling fuel costs with tonnage/distance.
* Tech advancements that allow increased capability.
* Proximity requirements for activation.
* Unique beacon models optimized for different transport scenarios.

The general process to use this network is to place at least two beacons, then approach one with a hailer (currently just MM'd into antennas).  The hailer allows you to open a dialog window with the active beacon, where it will tell you what other active beacons it can see and if it has enough fuel to send you there.  Assuming it does on both counts, press the button and off you go.  

Fuel is an important consideration, because it's what keeps the beacon system balanced in career mode.  True to the maxim that you can choose two from the list of fast, cheap and high-quality, the jump fuel that makes the system tick is incredibly costly, to the extent that most transfers the beacon system performs are more expensive than or comparable to traditional rocketry-based solutions.  Time is the ONLY thing you save with beacons, and some players may even plow more into logistics than they would otherwise have cause to do if they feel like maintaining an accessible and fueled beacon network at all times.  
This project is still in the early stages.  I'll note that this is my very first time:
* Running a GitHub repository.
* Writing code in C#.
* Modeling in Blender.
* Modeling in Unity.

I had used Photoshop before, so I have that going for me, which is nice.

Credit goes to ParameciumKid for inspiring me to work on my own version of a jumpdrive mod with his own excellent mod, and for being nice when I asked for pointers.  

Part.cfg changes powered by sarbian & ialdabaoth's ModuleManager plugin; used with permission:
http://forum.kerbalspaceprogram.com/threads/55219
