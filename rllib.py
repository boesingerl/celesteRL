from celeste_rl.level import *
from celeste_rl.env import *
# from celeste_rl.models import *
from rtgym import DEFAULT_CONFIG_DICT
# from stable_baselines3.common.monitor import Monitor
# from stable_baselines3 import PPO
from collections import OrderedDict

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

from celeste_rl.env import CelesteImgGym
import gymnasium as gym
from ray.rllib.algorithms.ppo import PPO, PPOConfig


from ray.air.integrations.wandb import WandbLoggerCallback

from gymnasium.envs.registration import EnvSpec
import ray
from gymnasium.wrappers import TimeLimit

class EnvCreator:
    
    def __init__(self, port=7777):
        self.port = port
    
    def get_env(self, config):
        env = TimeLimit(CelesteImgGym(self.port), max_episode_steps=2000)
        self.port += 1
        return env
    
    
from ray.tune.registry import register_env

creator = EnvCreator()
register_env("Celeste", creator.get_env)

config = PPOConfig()

# config.replay_buffer_config.update(
#      {
#          "capacity": 300000,
#          "type": "MultiAgentReplayBuffer",
#          "prioritized_replay": True,
#          "prioritized_replay_alpha": 0.6,
#           # Beta parameter for sampling from prioritized replay buffer.
#          "prioritized_replay_beta": 0.4
#      }
# )

config.model.update(use_lstm=True,
                    lstm_cell_size=64,
                    max_seq_len=20)

config.num_envs_per_worker = 8
config.num_cpus_per_worker = 4
config.train_batch_size = 10_000
config.framework = 'tf2'

# config.exploration_config = {
#         "type": "SoftQ",
#         "temperature": 1.0,
#     }

# config.lr = 1e-4
        
#config.compress_observations = True
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

#algo = R2D2(config=config)  
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
