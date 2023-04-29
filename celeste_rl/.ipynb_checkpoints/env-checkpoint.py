import json

import gymnasium as gym
import matplotlib.pyplot as plt
import numpy as np
import zmq
from gymnasium import spaces
from stable_baselines3.common.callbacks import BaseCallback

from .level import LevelRenderer

import json
import logging

import gymnasium as gym
import gymnasium.spaces as spaces
import numpy as np
import wandb
import zmq
from rtgym import RealTimeGymInterface
from stable_baselines3.common.callbacks import BaseCallback
from stable_baselines3.common.logger import TensorBoardOutputFormat


class CelesteGym(RealTimeGymInterface):

    def __init__(self, render_mode='rgb_array'):
        # Communication to C# component.
        
        self.context = None
        self.initialized = False
        self.latest_obs = None
        self.render_mode = render_mode

    def get_observation_space(self):
        return spaces.Tuple((spaces.Box(
            low=0, high=1, shape=(LevelRenderer.max_idx+1,
                                  LevelRenderer.VISION_SIZE*LevelRenderer.RESCALE,
                                  LevelRenderer.VISION_SIZE*LevelRenderer.RESCALE), dtype=np.float64
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
            obs = LevelRenderer.create_obs(obs_dic)
            
            self.latest_obs = obs
            
        return [obs], {}

    def get_obs_rew_terminated_info(self):
        obs_dic, reward, terminated = json.loads(self.socket.recv())
        obs = LevelRenderer.create_obs(obs_dic)
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
                        {"video": wandb.Video(rendered[:, :3, ...]*255, fps=30, format="webm")})
                    
                    self.renders = self.renders[ep_len:]
                    self.current_ep += 1
                    self.current_step += ep_len
                    
            
            self.tb_formatter.writer.flush()

    def _on_rollout_end(self) -> None:
        """
        This event is triggered before updating the policy.
        """

    def _on_training_end(self) -> None:
        """
        This event is triggered before exiting the `learn()` method.
        """


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
            low=0, high=1, shape=(LevelRenderer.max_idx+1,
                                  LevelRenderer.VISION_SIZE*LevelRenderer.RESCALE,
                                  LevelRenderer.VISION_SIZE*LevelRenderer.RESCALE), dtype=np.float64
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
