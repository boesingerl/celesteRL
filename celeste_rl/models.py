from stable_baselines3.common.torch_layers import BaseFeaturesExtractor

import gymnasium as gym
import torch as th
from gymnasium import spaces
from torch import nn
from stable_baselines3.common.torch_layers import NatureCNN
from .level import LevelRenderer

import gymnasium.spaces as spaces


import gymnasium as gym


class ImprovisedCNN(BaseFeaturesExtractor):
    """
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
        features_dim: int = 64,
    ) -> None:
        assert isinstance(observation_space, spaces.Box), (
            "NatureCNN must be used with a gym.spaces.Box ",
            f"observation space, not {observation_space}",
        )
        super().__init__(observation_space, features_dim)

        self.n_space = observation_space.shape[0]-1
        self.cnn = nn.Sequential(
            nn.Conv2d(self.n_space*2, self.n_space*4, kernel_size=8, stride=4, groups=LevelRenderer.max_idx, padding=0),
            nn.ReLU(),
            nn.Conv2d(self.n_space*4, 16, kernel_size=4, stride=2, padding=0),
            nn.ReLU(),
            nn.Conv2d(16, 4, kernel_size=3, stride=1, padding=0),
            nn.ReLU(),
            nn.Flatten(),
        )

        self.linear = nn.Sequential(nn.LazyLinear(features_dim), nn.ReLU())
        
        self.forward(th.ones((1, self.n_space*4, observation_space.shape[1], observation_space.shape[2])))

    def forward(self, observations: th.Tensor) -> th.Tensor:
        fullobs = th.empty((observations.shape[0],self.n_space*2,observations.shape[-2],observations.shape[-1]))

        for i in range(len(LevelRenderer.ID_MAP)):
            fullobs[:, i*2] = observations[:, i]
            fullobs[:, (i*2)+1] = observations[:, LevelRenderer.ID_MAP['Player']]

        # fullinp = th.cat([observations[:, [i,LevelRenderer.ID_MAP['Player']]] for i in range(LevelRenderer.max_idx+1) if i != LevelRenderer.ID_MAP['Player']], dim=1)
        return self.linear(self.cnn(fullobs))

class CustomCombinedExtractor(BaseFeaturesExtractor):
    def __init__(self, observation_space: spaces.Dict):
        # We do not know features-dim here before going over all the items,
        # so put something dummy for now. PyTorch requires calling
        # nn.Module.__init__ before adding modules
        super().__init__(observation_space, features_dim=1)

        extractors = {}

        actual_observes = observation_space.spaces
        vector_spaces = []
        
        total_concat_size = 0
        # We need to know size of the output of this extractor,
        # so go over all the spaces and compute output feature sizes
        for key, subspace in actual_observes.items():
            if key == "image":
                # We will just downsample one channel of the image by 4x4 and flatten.
                # Assume the image is single-channel (subspace.shape[0] == 0)
                
                extractor = ImprovisedCNN(subspace)
                extractors[key] = extractor
                total_concat_size += extractor.linear[0].out_features
                
            # assume flat vectors
            else:
                vector_spaces.append(subspace)

        # create vector extractor
        input_shape = sum(x.shape[0] for x in vector_spaces)
        vector_extractor = nn.Sequential(
            nn.Linear(input_shape, 16),
            nn.ReLU(),
            nn.Linear(16, 16))
        total_concat_size += 16
        extractors['vectors'] = vector_extractor
        
        self.extractors = nn.ModuleDict(extractors)

        # Update the features dim manually
        self._features_dim = total_concat_size

    def forward(self, observations) -> th.Tensor:
        encoded_tensor_list = []

        obs_dic = observations
        vector_obs = []
        
        # self.extractors contain nn.Modules that do all the processing.
        for key, obs in obs_dic.items():
            
            if key in self.extractors:
                encoded_tensor_list.append(self.extractors[key](obs))
            else:
                vector_obs.append(obs)
            
        encoded_tensor_list.append(self.extractors['vectors'](th.cat(vector_obs, dim=1)))
        
        # Return a (B, self._features_dim) PyTorch tensor, where B is batch dimension.
        return th.cat(encoded_tensor_list, dim=1)


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

