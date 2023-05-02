#!/usr/bin/env python
# coding: utf-8

from celeste_rl.level import *
from celeste_rl.env import *
from celeste_rl.models import *
from celeste_rl.schedule import *

from rtgym import DEFAULT_CONFIG_DICT
from stable_baselines3.common.monitor import Monitor
from stable_baselines3 import PPO
from collections import OrderedDict

import matplotlib.pyplot as plt
import numpy as np
import cv2
import gymnasium


# In[ ]:


# In[10]:

my_config = DEFAULT_CONFIG_DICT
my_config["interface"] = CelesteGym
my_config["time_step_duration"] = 0.065
my_config["start_obs_capture"] = 0.065
my_config["time_step_timeout_factor"] = 1.0
my_config["ep_max_length"] = 256
my_config["act_buf_len"] = 10
my_config["reset_act_buf"] = False
my_config["benchmark"] = True
my_config["benchmark_polyak"] = 0.2

run = wandb.init(
    project="celesterl",
    config=my_config,
    sync_tensorboard=True,  # auto-upload sb3's tensorboard metrics
    monitor_gym=True,  # auto-upload the videos of agents playing the game
    save_code=True,  # optional
)

env = gymnasium.make("real-time-gym-v1", config=my_config)
cyclic_schedule = CyclicLR(step_size=0.5,base_lr=0.00001, max_lr=0.0001)

wrapenv = Monitor(FlattenerEnv(env))
model = PPO("MultiInputPolicy",
            wrapenv,
            n_steps=512,
            policy_kwargs=dict(normalize_images=False,
                              features_extractor_class=CustomCombinedExtractor),
            learning_rate=cyclic_schedule.adapted_clr,
            verbose=1,
            device='cpu',
            tensorboard_log="./celeste_tensorboard/")

for i in range(10):

    model.learn(100_000)
    
    model.save(f'model2_{i}.sav')


