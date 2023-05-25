import json

import gymnasium as gym
import matplotlib.pyplot as plt
import numpy as np
import zmq
from gymnasium import spaces
#from stable_baselines3.common.callbacks import BaseCallback
#from stable_baselines3.common.callbacks import BaseCallback
#from stable_baselines3.common.logger import TensorBoardOutputFormat
from .level import LevelRenderer

import json
import logging

import gymnasium as gym
import gymnasium.spaces as spaces
import numpy as np
import wandb
import zmq
from rtgym import RealTimeGymInterface
from collections import OrderedDict
import base64
import PIL.Image as Image
import io
import json
from gymnasium.envs.registration import EnvSpec

class CelesteImgGym(gym.Env):
    metadata = {"render_modes": ["human", "rgb_array"], "render_fps": 4}

    def __init__(self, port, render_mode=None):

        self.port = port
        self.context = None
        self.initialized = False
        self.render_mode = render_mode
        
        
        self.spec = EnvSpec('Celeste', entry_point=CelesteImgGym, max_episode_steps=2000)
        
        
        self.observation_space = spaces.Dict({'image':spaces.Box(
            low=0, high=255, shape=(42,
                                  42,
                                 3), dtype=np.float32)
                                         })
        #self.action_space = spaces.Tuple((spaces.Discrete(2), spaces.Discrete(2)))
        
        
        

        # We have 4 actions, corresponding to "right", "up", "left", "down"
        # self.action_space = spaces.MultiBinary(7)
        
        
        #left, right, up, down, jump, dash, grab
        self.left  = np.array([1,0,0,0,0,0,0])
        self.right = np.array([0,1,0,0,0,0,0])
        self.up    = np.array([0,0,1,0,0,0,0])
        self.down  = np.array([0,0,0,1,0,0,0])
        
        self.jump = np.array([0,0,0,0,1,0,0])
        self.dash = np.array([0,0,0,0,0,1,0])
        self.grab = np.array([0,0,0,0,0,0,1])
        
        self.actions = {'left': self.left,
                        'right': self.right,
                        #'up': self.up,
                        #'down':self.down,
                        'jump':self.jump,
                        'jump_left':self.jump + self.left,
                        'jump_right':self.jump + self.right,
                        'grab_up': self.grab + self.up,
                        'grab_down': self.grab + self.down,
                        #'dash_dleft': self.dash + self.down + self.left,
                        #'dash_dright': self.dash + self.down + self.right,
                        'dash_uleft': self.dash + self.up + self.left,
                        'dash_uright': self.dash + self.up + self.right}
        self.action_values = list(self.actions.values())
        
        self.action_space = spaces.Discrete(len(self.action_values))
         

        
        
        
    def _get_obs(self, obs):
        return {'image':np.array(Image.open(io.BytesIO(base64.b64decode(obs))))[..., :3]}

    
    def _send_control(self, action):
        action = self.action_values[action].tolist()
        msg = json.dumps(action)
        self.socket.send_string(msg)

    def reset(self, seed=None, options=None):
        logging.warning(f'RESET WITH PORT {self.port}')
        
        if self.initialized:
            self.socket.close()
            self.context.destroy()            
            self.initialized = False
        if not self.initialized:
            self.context = zmq.Context()
            self.socket = self.context.socket(zmq.REQ)
            self.socket.connect(f"tcp://localhost:{self.port}")
            self.initialized = True
            
            self.socket.send_string(json.dumps([1]))
            obs_dic, _, _ = json.loads(self.socket.recv())
        return self._get_obs(obs_dic), {}

    def step(self, action):
        self._send_control(action)
        return self._get_obs_rew_terminated_info()
    
    def close(self):
        self.socket.close()
        self.context.destroy()
        self.initialized = False
    
    def _get_obs_rew_terminated_info(self):
        obs_dic, reward, terminated = json.loads(self.socket.recv())
        info = {}
        return self._get_obs(obs_dic), reward, terminated, False, info

