#!/usr/bin/env python
# coding: utf-8

# In[1]:


import zmq
import time
import sys
import json
import matplotlib.pyplot as plt
import numpy as np
import jmespath
from IPython.display import clear_output
import time
import pickle
import cv2

from stable_baselines3 import PPO

from stable_baselines3.common.preprocessing import get_obs_shape
from stable_baselines3.common.monitor import Monitor

TILESIZE = 8

from rtgym import RealTimeGymInterface, DEFAULT_CONFIG_DICT, DummyRCDrone
import gymnasium.spaces as spaces
import gymnasium
import numpy as np
import cv2

import time
import pprint

from gymnasium.spaces import Box, MultiBinary
from numpy.typing import NDArray
import gymnasium as gym
import json
import numpy as np
import time
import zmq
from typing import Optional, Tuple
import logging
import wandb
from wandb.integration.sb3 import WandbCallback

# In[2]:


class LevelRenderer:
    
    TILE_SIZE = 8
    VOID_TEXTURES = [10]
    
    
    ID_MAP = {'CrumblePlatform': 1,
             'DashBlock': 2,
             'FallingBlock': 3,
             'JumpthruPlatform': 4,
             'Player': 5,
             'Refill': 6,
             'Spikes': 7,
             'Spring': 8,
             'ZipMover': 9,
             }
    VISION_SIZE = 48
        
    max_idx = max(ID_MAP.values())
    entity_values = range(max_idx+1)

    norm = plt.Normalize(vmin=0, vmax=max_idx)
    cm = plt.cm.nipy_spectral
        
    def __init__(self, img, entities, bounds):
        
        self.img = np.zeros((img.shape[0], img.shape[1], LevelRenderer.max_idx+1))
        self.img[:,:,0] = img
        self.bounds = bounds
        self.entities = entities
        
        for entity in self.entities:
            if entity['Name'] in LevelRenderer.ID_MAP:
                self.generic_handler(entity)
                
        
    def render_around_player(self):
        pad0 =  np.pad(self.img[:, :, 0:1], ((LevelRenderer.VISION_SIZE,),(LevelRenderer.VISION_SIZE,), (0,)), constant_values=1)
        padrest =  np.pad(self.img[:,:,1:], ((LevelRenderer.VISION_SIZE,),(LevelRenderer.VISION_SIZE,), (0,)))

        padded = np.dstack((pad0, padrest))
        ys, xs = np.where(padded[:,:,LevelRenderer.ID_MAP['Player']])

        if len(xs) > 0 and len(ys) > 0:
            y = ys[0]
            x = xs[0]
            
            
        # player is spawning, set to bottom left corner
        else:
            y = self.img.shape[0]-1 + LevelRenderer.VISION_SIZE//2
            x = 0 + LevelRenderer.VISION_SIZE//2
            
        return padded[y-LevelRenderer.VISION_SIZE//2:y+LevelRenderer.VISION_SIZE//2,
                      x-LevelRenderer.VISION_SIZE//2:x+LevelRenderer.VISION_SIZE//2]
        
    @staticmethod
    def color_to_idx(img):
    
        color_dict = {i:tuple([int(255*x) for x in LevelRenderer.cm(LevelRenderer.norm(i))]) for i in LevelRenderer.entity_values}
        rev_dict = {b:a for a,b in color_dict.items()}

        restruc = nlr.unstructured_to_structured(img).astype('O')

        return np.vectorize(rev_dict.get)(restruc)

    def render_finish(self, dim1, dim2):
        tmp = self.img.copy()
        tmp[dim1, dim2] = LevelRenderer.ID_MAP['finish']
        return LevelRenderer.cm(LevelRenderer.norm(tmp))
    
    def generic_handler(self, entity, x_offset=0, y_offset=0, width_override=None, height_override=None):

        left = int(np.floor((int(entity['Left'])- self.bounds['X']) / LevelRenderer.TILE_SIZE))
        right = int(np.ceil((int(entity['Right']) - self.bounds['X']) / LevelRenderer.TILE_SIZE))
        top = int(np.floor((int(entity['Top']) - self.bounds['Y']) / LevelRenderer.TILE_SIZE))
        bottom = int(np.ceil((int(entity['Bottom']) - self.bounds['Y']) / LevelRenderer.TILE_SIZE))

        self.img[top:bottom, left:right, LevelRenderer.ID_MAP[entity['Name']]] = 1


# In[3]:


def create_obs(obs_dic):
    entities = [dict(ent, Name=ent['Name'].split('Celeste.')[-1]) for ent in obs_dic['entities']]
    bounds = json.loads(obs_dic['bounds']
              .replace(' ', ',')
              .replace('X', '"X"')
              .replace('Height', '"Height"')
              .replace('Width', '"Width"')
              .replace('Y', '"Y"')
             )
    width = int(np.ceil(bounds['Width']/TILESIZE))
    height = int(np.ceil(bounds['Height']/TILESIZE))

    solids = np.array([list(x + ('0'*(width - len(x)))) for x in obs_dic['solids'].split('\n')])
    solids = np.where(solids == "0", 0, 1)
    
    fullimg = LevelRenderer(solids, entities, bounds).img
    # plt.imshow(fullimg.argmax(-1) + fullimg[:,:,0])
    # plt.show()
    lvl = LevelRenderer(solids, entities, bounds).render_around_player().transpose(2,0,1)
    
    return lvl




class CelesteGym(RealTimeGymInterface):

    def __init__(self, render_mode='rgb_array'):
        # Communication to C# component.
        
        self.context = None
        self.initialized = False
        self.latest_obs = None
        self.render_mode = render_mode

    def get_observation_space(self):
        return spaces.Tuple((spaces.Box(
            low=0, high=1, shape=(LevelRenderer.max_idx+1,LevelRenderer.VISION_SIZE, LevelRenderer.VISION_SIZE), dtype=np.float64
        ),))

    def get_action_space(self):
        return spaces.MultiBinary(7)

    def get_default_action(self):
        return np.array([0,0,0,0,0,0,0], dtype='int8')

    def send_control(self, action):
        action = action.astype(np.uint8).tolist()
        msg = json.dumps(action)
        self.socket.send_string(msg)

    def reset(self, seed=None, options=None):
        logging.warning('RESET')
        
        if self.initialized:
            self.socket.close()
            self.context.destroy()            
            self.initialized = False
        if not self.initialized:
            self.context = zmq.Context()
            self.socket = self.context.socket(zmq.REQ)
            self.socket.connect("tcp://localhost:7777")
            self.initialized = True
            
            self.socket.send_string(json.dumps([1]))
            obs_dic, _, _ = json.loads(self.socket.recv())
            obs = create_obs(obs_dic)
            
            self.latest_obs = obs
            
        return [obs], {}

    def get_obs_rew_terminated_info(self):
        obs_dic, reward, terminated = json.loads(self.socket.recv())
        obs = create_obs(obs_dic)
        self.latest_obs = obs
        
        info = {}
        return [obs], reward, terminated, info

    def wait(self):
        pass
    
    def render(self, mode='rgb_array'):
        pass
    
    def render_img(self, mode='rgb_array'):
        img = self.latest_obs.argmax(0) + self.latest_obs[0]
        
        norm = plt.Normalize(vmin=0, vmax=LevelRenderer.max_idx)
        cm = plt.cm.nipy_spectral
        
        return cm(norm(img))
        


from stable_baselines3.common.callbacks import BaseCallback
from stable_baselines3.common.torch_layers import BaseFeaturesExtractor
from stable_baselines3.common.preprocessing import get_flattened_obs_dim, is_image_space
from stable_baselines3.common.type_aliases import TensorDict
from stable_baselines3.common.utils import get_device
from typing import Dict, List, Tuple, Type, Union

import gymnasium as gym
import torch as th
from gymnasium import spaces
from torch import nn

class FeatureCNN(BaseFeaturesExtractor):
    """
    CNN from DQN Nature paper:
        Mnih, Volodymyr, et al.
        "Human-level control through deep reinforcement learning."
        Nature 518.7540 (2015): 529-533.

    :param observation_space:
    :param features_dim: Number of features extracted.
        This corresponds to the number of unit for the last layer.
    :param normalized_image: Whether to assume that the image is already normalized
        or not (this disables dtype and bounds checks): when True, it only checks that
        the space is a Box and has 3 dimensions.
        Otherwise, it checks that it has expected dtype (uint8) and bounds (values in [0, 255]).
    """

    def __init__(
        self,
        observation_space: gym.Space,
        features_dim: int = 512,
        normalized_image: bool = False,
    ) -> None:
        assert isinstance(observation_space, spaces.Box), (
            "NatureCNN must be used with a gym.spaces.Box ",
            f"observation space, not {observation_space}",
        )
        super().__init__(observation_space, features_dim)
        # We assume CxHxW images (channels first)
        # Re-ordering will be done by pre-preprocessing or wrapper

        n_input_channels = observation_space.shape[0]
        self.cnn = nn.Sequential(
            nn.Conv2d(n_input_channels, 16, kernel_size=5, stride=3, padding=0),
            nn.ReLU(),
            nn.Conv2d(16, 16, kernel_size=3, stride=1, padding=0),
            nn.ReLU(),
            nn.Conv2d(16, 16, kernel_size=3, stride=1, padding=0),
            nn.ReLU(),
            nn.Flatten(),
        )

        # Compute shape by doing one forward pass
        with th.no_grad():
            n_flatten = self.cnn(th.as_tensor(observation_space.sample()[None]).float()).shape[1]

        self.linear = nn.Sequential(nn.Linear(n_flatten, features_dim), nn.ReLU())

    def forward(self, observations: th.Tensor) -> th.Tensor:
        return self.linear(self.cnn(observations))


from stable_baselines3.common.callbacks import BaseCallback
from stable_baselines3.common.logger import TensorBoardOutputFormat

            
class CustomCallback(BaseCallback):
    """
    A custom callback that derives from ``BaseCallback``.

    :param verbose: Verbosity level: 0 for no output, 1 for info messages, 2 for debug messages
    """
    def __init__(self, verbose=0):
        super(CustomCallback, self).__init__(verbose)
        # Those variables will be accessible in the callback
        # (they are defined in the base class)
        # The RL model
        # self.model = None  # type: BaseAlgorithm
        # An alias for self.model.get_env(), the environment used for training
        # self.training_env = None  # type: Union[gym.Env, VecEnv, None]
        # Number of time the callback was called
        # self.n_calls = 0  # type: int
        # self.num_timesteps = 0  # type: int
        # local and global variables
        # self.locals = None  # type: Dict[str, Any]
        # self.globals = None  # type: Dict[str, Any]
        # The logger object, used to report things in the terminal
        # self.logger = None  # stable_baselines3.common.logger
        # # Sometimes, for event callback, it is useful
        # # to have access to the parent object
        # self.parent = None  # type: Optional[BaseCallback]
        self.renders = []
        self.current_ep = 0
        self.current_step = 0


    def _on_training_start(self) -> None:
        """
        This method is called before the first rollout starts.
        """
        self._log_freq = 1000  # log every 1000 calls

        output_formats = self.logger.output_formats
        # Save reference to tensorboard formatter object
        # note: the failure case (not formatter found) is not handled here, should be done with try/except.
        self.tb_formatter = next(formatter for formatter in output_formats if isinstance(formatter, TensorBoardOutputFormat))


    def _on_rollout_start(self) -> None:
        """
        A rollout is the collection of environment interaction
        using the current policy.
        This event is triggered before collecting new samples.
        """
        pass

    def _on_step(self) -> bool:
        """
        This method will be called by the model after each call to `env.step()`.

        For child callback (of an `EventCallback`), this will be called
        when the event is triggered.

        :return: (bool) If the callback returns False, training is aborted early.
        """
        rendered = self.training_env.render(mode='rgb_array').transpose((2,0,1))
        self.renders.append(rendered)
        
        if self.n_calls % self._log_freq == 0:
            # You can have access to info from the env using self.locals.
            # for instance, when using one env (index 0 of locals["infos"]):
            # lap_count = self.locals["infos"][0]["lap_count"]
            # self.tb_formatter.writer.add_scalar("train/lap_count", lap_count, self.num_timesteps)

            episode_lens = self.training_env.envs[0].get_episode_lengths()[self.current_ep:]
            
            if len(episode_lens) > 0:
                for ep_len in episode_lens:
                    
                    rendered = np.stack(self.renders[:ep_len])
                    wandb.log(
                        {"video": wandb.Video(rendered[:, :3, ...]*255, fps=60, format="webm")})
                    
                    self.renders = self.renders[ep_len:]
                    self.current_ep += 1
                    self.current_step += ep_len
                    
            
            self.tb_formatter.writer.flush()

    def _on_rollout_end(self) -> None:
        """
        This event is triggered before updating the policy.
        """
        pass

    def _on_training_end(self) -> None:
        """
        This event is triggered before exiting the `learn()` method.
        """
        pass


class CustomEnv(gym.Env):
    """Custom Environment that follows gym interface."""

    def __init__(self, env, render_mode='rgb_array'):
        super().__init__()
        # Define action and observation space
        # They must be gym.spaces objects
        # Example when using discrete actions:
        self.action_space = spaces.MultiBinary(7)
        # Example for using image as input (channel-first; channel-last also works):
        self.observation_space = spaces.Box(
            low=0, high=1, shape=(LevelRenderer.max_idx+1,LevelRenderer.VISION_SIZE, LevelRenderer.VISION_SIZE), dtype=np.float64
        )
        self.env = env
        self.render_mode = render_mode

    def step(self, action):
        obs, rew, terminated, truncated, info = self.env.step(action)
        return obs[0], rew, terminated, truncated, info

    def reset(self):
        obs, info = self.env.reset()
        return obs[0], info
    def render(self, mode='rgb_array'):
        rend =  self.env.env.env.interface.render_img()
        return rend

    def close(self):
        self.env.close()


# In[10]:

my_config = DEFAULT_CONFIG_DICT
my_config["interface"] = CelesteGym
my_config["time_step_duration"] = 0.025
my_config["start_obs_capture"] = 0.025
my_config["time_step_timeout_factor"] = 1.0
my_config["ep_max_length"] = 1_000_000
my_config["act_buf_len"] = 4
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
wrapenv = Monitor(CustomEnv(env))
obs_space = env.observation_space
act_space = env.action_space

model = PPO("CnnPolicy",
            wrapenv,
            n_steps=2048,
            learning_rate=5e-4,
            policy_kwargs=dict(normalize_images=False, features_extractor_class=FeatureCNN),
            verbose=1,
            device='cpu',
            tensorboard_log="./celeste_tensorboard/")




model.learn(1_000_000, callback=CustomCallback())


model.save('model.sav')



