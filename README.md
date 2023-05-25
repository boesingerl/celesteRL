# Celeste Reinforcement Learning

<p align="center">
  <img src="https://github.com/boesingerl/celesteRL/assets/32189761/7888de49-080a-4b02-a77e-0f9525e52a80" width="50%"/>
</p>

This repo aims to train a reinforcement learning agent that is able to solve Celeste levels.

It works by communicating observations using an [Everest](https://everestapi.github.io/) mod with a python gym environment, that currently uses rllib to train agents.


## Notable mentions

I would like to thank all maintainers of these Everest mods, from which I reused code:

- https://github.com/EverestAPI/CelesteTAS-EverestInterop (Simplified graphics, Centering camera, frame stepping, gamepad updates when window is not focused, etc..)
- https://github.com/DemoJameson/CelesteSpeedrunTool (Speeding up death screens)
- https://github.com/sc2ad/CelesteBot (Plotting, general ideas)
- https://github.com/iSkytran/ctrl (Help with NetMQ)
