

import matplotlib.pyplot as plt
import numpy as np
import cv2
import gymnasium

import zmq
import numpy as np
import matplotlib.pyplot as plt
import cv2

import PIL.Image as Image
import io
import json
import base64

import gymnasium as gym

import ray

from celeste_rl.env import CelesteImgGym
from celeste_rl.level import *
from celeste_rl.env import *
from rtgym import DEFAULT_CONFIG_DICT
from collections import OrderedDict

from gymnasium.envs.registration import EnvSpec
from gymnasium.wrappers import TimeLimit

from ray.rllib.algorithms.ppo import PPO, PPOConfig
from ray.air.integrations.wandb import WandbLoggerCallback
from ray.tune.registry import register_env


class EnvCreator:
    
    def __init__(self, port=7777):
        self.port = port
    
    def get_env(self, config):
        env = TimeLimit(CelesteImgGym(self.port), max_episode_steps=2000)
        self.port += 1
        return env
    
    


creator = EnvCreator()
register_env("Celeste", creator.get_env)

config = PPOConfig()

config.model.update(use_lstm=True,
                    lstm_cell_size=64,
                    max_seq_len=20)

config.num_envs_per_worker = 8
config.num_cpus_per_worker = 4
config.train_batch_size = 10_000
config.framework = 'tf2'

(config
 .training(
           lr_schedule=[[0, 1e-4],[1000*1_000, 5e-4], [2000*1_000, 3e-4], [10_000*1_000, 1e-4]]
           )
 .resources(num_gpus=1)
 .rollouts(num_rollout_workers=1)
 .environment("Celeste")
 .checkpointing(export_native_model_files=True)
)

print(config.exploration_config)

analysis = ray.tune.run(PPO,
                        config=config,
                        stop={"training_iteration": 10_000},
                        checkpoint_at_end=True,
                        callbacks=[WandbLoggerCallback('celesterllib')],
                        checkpoint_freq=3,
                        keep_checkpoints_num=3,
                        checkpoint_score_attr='episode_reward_mean',
                        local_dir="results"
                        )
