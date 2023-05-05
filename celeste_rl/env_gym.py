import json

import gym
import matplotlib.pyplot as plt
import numpy as np
import zmq
from .level import LevelRenderer

import json
import logging

import numpy as np
import wandb
import zmq
from collections import OrderedDict
import cv2
import gym
from gym import spaces

class CelesteGym(gym.Env):
    metadata = {"render_modes": ["human", "rgb_array"], "render_fps": 4}

    def __init__(self, render_mode=None, scale=8, vision_size=24, resize=64, interp=cv2.INTER_AREA):

        self.context = None
        self.initialized = False
        self.latest_obs = None
        self.render_mode = render_mode
        self.scale = scale
        self.vision_size = vision_size
        
        self.observation_space = spaces.Dict({'image':spaces.Box(
            low=0, high=1, shape=(resize,
                                  resize,
                                 3), dtype=np.float32),
                                          'climbing': spaces.Box(low=0, high=1, shape=(1,)),
                                          'canDash': spaces.Box(low=0, high=1, shape=(1,)),
                                          'speeds': spaces.Box(low=float('-inf'), high=float('inf'),shape=(2,), dtype=np.float32),
                                         })
        
        
        

        # We have 4 actions, corresponding to "right", "up", "left", "down"
        # self.action_space = spaces.MultiBinary(7)
        self.action_space = spaces.Box(low=-1, high=1, shape=(7,), dtype=np.float32)
        self.resize = resize
        self.interp = interp
        
    def _get_obs(self, obs):
        img = obs['image']
        img = img[0] + img.argmax(0)
        img = LevelRenderer.cm(LevelRenderer.norm(img))[...,:3]
        obs['image'] = cv2.resize(img, (self.resize, self.resize), interpolation= self.interp)
 
        return obs
    
    def _send_control(self, action):
        action = action.tolist()
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
            obs = LevelRenderer.create_obs(obs_dic, self.scale, self.vision_size)
            
            self.latest_obs = obs
            
        return self._get_obs(obs)

    def step(self, action):
        self._send_control(action)
        return self._get_obs_rew_terminated_info()
    
    def close(self):
        self.socket.close()
        self.context.destroy()
        self.initialized = False
    
    def _get_obs_rew_terminated_info(self):
        obs_dic, reward, terminated = json.loads(self.socket.recv())
        obs = LevelRenderer.create_obs(obs_dic, self.scale, self.vision_size)
        self.latest_obs = obs
        
        info = {}
        return self._get_obs(obs), reward, terminated, info



# class CelesteGymRT(RealTimeGymInterface):

#     def __init__(self, render_mode='rgb_array', scale=4, vision_size=24):
#         # Communication to C# component.
        
#         self.context = None
#         self.initialized = False
#         self.latest_obs = None
#         self.render_mode = render_mode
#         self.scale = scale
#         self.vision_size = vision_size

    # def get_observation_space(self):
    #     return spaces.Tuple((spaces.Dict({'image':spaces.Box(
    #         low=0, high=1, shape=(LevelRenderer.max_idx+1,
    #                               self.vision_size*self.scale,
    #                               self.vision_size*self.scale), dtype=np.float32),
    #                                       'climbing': spaces.Box(low=0, high=1, shape=(1,)),
    #                                       'canDash': spaces.Box(low=0, high=1, shape=(1,)),
    #                                       'speeds': spaces.Box(low=float('-inf'), high=float('inf'),shape=(2,), dtype=np.float32),
    #                                      }),))

#     def get_action_space(self):
#         return spaces.MultiBinary(7)

#     def get_default_action(self):
#         return np.array([0,0,0,0,0,0,0], dtype='float32')

#     def send_control(self, action):
#         action = action.tolist()
#         msg = json.dumps(action)
#         self.socket.send_string(msg)

#     def reset(self, seed=None, options=None):
#         logging.warning('RESET')
        
#         if self.initialized:
#             self.socket.close()
#             self.context.destroy()            
#             self.initialized = False
#         if not self.initialized:
#             self.context = zmq.Context()
#             self.socket = self.context.socket(zmq.REQ)
#             self.socket.connect("tcp://localhost:7777")
#             self.initialized = True
            
#             self.socket.send_string(json.dumps([1]))
#             obs_dic, _, _ = json.loads(self.socket.recv())
#             obs = LevelRenderer.create_obs(obs_dic, self.scale, self.vision_size)
            
#             self.latest_obs = obs
            
#         return [obs], {}

#     def get_obs_rew_terminated_info(self):
#         obs_dic, reward, terminated = json.loads(self.socket.recv())
#         obs = LevelRenderer.create_obs(obs_dic, self.scale, self.vision_size)
#         self.latest_obs = obs
        
#         info = {}
#         return [obs], reward, terminated, info

#     def wait(self):
#         pass
    
#     def render(self, mode='rgb_array'):
#         pass
    
#     def render_img(self, mode='rgb_array'):
#         img = self.latest_obs.argmax(0) + self.latest_obs[0]
        
#         norm = plt.Normalize(vmin=0, vmax=LevelRenderer.max_idx)
#         cm = plt.cm.nipy_spectral
        
#         return cm(norm(img))
        
        
           
# class CustomCallback(BaseCallback):
#     """
#     A custom callback that derives from ``BaseCallback``.

#     :param verbose: Verbosity level: 0 for no output, 1 for info messages, 2 for debug messages
#     """
#     def __init__(self, verbose=0):
#         super(CustomCallback, self).__init__(verbose)
#         # Those variables will be accessible in the callback
#         # (they are defined in the base class)
#         # The RL model
#         # self.model = None  # type: BaseAlgorithm
#         # An alias for self.model.get_env(), the environment used for training
#         # self.training_env = None  # type: Union[gym.Env, VecEnv, None]
#         # Number of time the callback was called
#         # self.n_calls = 0  # type: int
#         # self.num_timesteps = 0  # type: int
#         # local and global variables
#         # self.locals = None  # type: Dict[str, Any]
#         # self.globals = None  # type: Dict[str, Any]
#         # The logger object, used to report things in the terminal
#         # self.logger = None  # stable_baselines3.common.logger
#         # # Sometimes, for event callback, it is useful
#         # # to have access to the parent object
#         # self.parent = None  # type: Optional[BaseCallback]
#         self.renders = []
#         self.current_ep = 0
#         self.current_step = 0


#     def _on_training_start(self) -> None:
#         """
#         This method is called before the first rollout starts.
#         """
#         self._log_freq = 1000  # log every 1000 calls

#         output_formats = self.logger.output_formats
#         # Save reference to tensorboard formatter object
#         # note: the failure case (not formatter found) is not handled here, should be done with try/except.
#         self.tb_formatter = next(formatter for formatter in output_formats if isinstance(formatter, TensorBoardOutputFormat))


#     def _on_rollout_start(self) -> None:
#         """
#         A rollout is the collection of environment interaction
#         using the current policy.
#         This event is triggered before collecting new samples.
#         """

#     def _on_step(self) -> bool:
#         """
#         This method will be called by the model after each call to `env.step()`.

#         For child callback (of an `EventCallback`), this will be called
#         when the event is triggered.

#         :return: (bool) If the callback returns False, training is aborted early.
#         """
#         rendered = self.training_env.render(mode='rgb_array').transpose((2,0,1))
#         self.renders.append(rendered)
        
#         if self.n_calls % self._log_freq == 0:
#             # You can have access to info from the env using self.locals.
#             # for instance, when using one env (index 0 of locals["infos"]):
#             # lap_count = self.locals["infos"][0]["lap_count"]
#             # self.tb_formatter.writer.add_scalar("train/lap_count", lap_count, self.num_timesteps)

#             episode_lens = self.training_env.envs[0].get_episode_lengths()[self.current_ep:]
            
#             if len(episode_lens) > 0:
#                 for ep_len in episode_lens:
                    
#                     rendered = np.stack(self.renders[:ep_len])
#                     wandb.log(
#                         {"video": wandb.Video(rendered[:, :3, ...]*255, fps=30, format="webm")})
                    
#                     self.renders = self.renders[ep_len:]
#                     self.current_ep += 1
#                     self.current_step += ep_len
                    
            
#             self.tb_formatter.writer.flush()

#     def _on_rollout_end(self) -> None:
#         """
#         This event is triggered before updating the policy.
#         """

#     def _on_training_end(self) -> None:
#         """
#         This event is triggered before exiting the `learn()` method.
#         """


# class ImageEnv(gym.Env):
#     """Custom Environment that follows gym interface."""

#     def __init__(self, env, render_mode='rgb_array'):
#         super().__init__()
#         # Define action and observation space
#         # They must be gym.spaces objects
#         # Example when using discrete actions:
#         self.action_space = env.action_space
#         # Example for using image as input (channel-first; channel-last also works):
        
#         og_shape = env.observation_space[0]['image'].shape
        
#         self.observation_space = spaces.Box(shape=(og_shape[-2], og_shape[-1], 3), dtype=np.float32, low=0, high=1)
#         self.env = env
        
#         self.render_mode = render_mode

#     @staticmethod
#     def _get_obs(obs):
#         img = obs[0]['image']
#         img = img[0] + img.argmax(0)
#         return LevelRenderer.cm(LevelRenderer.norm(img))[...,:3]
    
#     def step(self, action):
#         obs, rew, terminated, truncated, info = self.env.step(action)
#         return ImageEnv._get_obs(obs), rew, terminated, truncated, info

#     def reset(self):
#         obs, info = self.env.reset()
#         return ImageEnv._get_obs(obs), info
    
#     def render(self, mode='rgb_array'):
#         rend =  self.env.env.env.interface.render_img()
#         return rend

#     def close(self):
#         self.env.close()
        
# class CustomEnv(gym.Env):
#     """Custom Environment that follows gym interface."""

#     def __init__(self, env, render_mode='rgb_array'):
#         super().__init__()
#         # Define action and observation space
#         # They must be gym.spaces objects
#         # Example when using discrete actions:
#         self.action_space = env.action_space
#         # Example for using image as input (channel-first; channel-last also works):
        
#         self.observation_space = env.observation_space[0]
#         self.env = env
        
#         self.render_mode = render_mode

#     def step(self, action):
#         obs, rew, terminated, truncated, info = self.env.step(action)
#         return obs[0], rew, terminated, truncated, info

#     def reset(self):
#         obs, info = self.env.reset()
#         return obs[0], info
#     def render(self, mode='rgb_array'):
#         rend =  self.env.env.env.interface.render_img()
#         return rend

#     def close(self):
#         self.env.close()

# class FlattenerEnv(gym.Env):
#     """Transform Tuple(Dict(Image), x, y ,z)) into Dict(Image, x, y, z) space."""

#     def __init__(self, env, render_mode='rgb_array'):
#         super().__init__()
#         # Define action and observation space
#         # They must be gym.spaces objects
#         # Example when using discrete actions:
#         self.action_space = env.action_space
#         # Example for using image as input (channel-first; channel-last also works):
        
#         self.observation_space = spaces.Dict(OrderedDict(list(env.observation_space[0].items())
#                                                          + [(f'action_{i}',s) for i,s in enumerate(env.observation_space[1:])]))
#         self.env = env
        
#         self.render_mode = render_mode

#     def _compute_obs(self, obs):
#         obsdict = obs[0].copy()
#         for i, x in enumerate(obs[1:]):
#             obsdict[f'action_{i}'] = (x > 0).astype(np.int8)  
#         return obsdict
    
#     def step(self, action):
#         obs, rew, terminated, truncated, info = self.env.step(action)
#         return self._compute_obs(obs), rew, terminated, truncated, info

#     def reset(self):
#         obs, info = self.env.reset()
        
#         obs_reset = self._compute_obs(obs)
        
#         return obs_reset, info
    
#     def render(self, mode='rgb_array'):
#         rend =  self.env.env.env.interface.render_img()
#         return rend

#     def close(self):
#         self.env.close()