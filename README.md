# Celeste Reinforcement Learning

https://github.com/boesingerl/celesteRL/assets/32189761/0e8670cd-6f92-468e-86a1-afb302597d9c


This repo aims to train a reinforcement learning agent that is able to solve Celeste levels.

It works by communicating observations using an [Everest](https://everestapi.github.io/) mod with a python gymnasium environment, that currently uses rllib to train agents.
It allows running multiple instances at the same time by obtaining observations directly from the rendering engine and allowing gamepad update while the window is not focused.

It is currently in an early state: everything works but it is not well optimized (high cpu usage), and the code is not modular.
Unfortunately, I haven't yet been able to train an agent that performed well (not even finishing the first level).

## Notable mentions

I would like to thank all maintainers of these Everest mods, from which I reused code:

- https://github.com/EverestAPI/CelesteTAS-EverestInterop (Simplified graphics, Centering camera, frame stepping, gamepad updates when window is not focused, etc..)
- https://github.com/DemoJameson/CelesteSpeedrunTool (Speeding up death screens)
- https://github.com/sc2ad/CelesteBot (Plotting, general ideas)
- https://github.com/iSkytran/ctrl (Help with NetMQ)
