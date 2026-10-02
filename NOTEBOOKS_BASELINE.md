# Notebook baseline

Generated 2026-10-02 14:24 by tools/notebook_probe.ps1 - do not edit by hand.

| Lesson | Unimportable modules | np names missing | Missing list |
|---|---|---|---|
| lesson01_images_as_arrays | cv2 matplotlib.pyplot | 0 |  |
| lesson02_image_arithmetic | cv2 matplotlib.pyplot | 0 |  |
| lesson03_thresholding_morphology | cv2 matplotlib.pyplot | 1 | random.default_rng |
| lesson04_floodfill_connected_components | cv2 matplotlib.pyplot | 0 |  |
| lesson05_moments | cv2 matplotlib.pyplot | 2 | linalg.eigh mgrid |
| lesson06_eigen_covariance | cv2 matplotlib.pyplot | 1 | linalg.eigh |
| lesson07_distance_measures | cv2 matplotlib.pyplot | 2 | diff mgrid |
| lesson08_geometric_transformations | cv2 matplotlib.pyplot | 0 |  |
| lesson09_warping_interpolation | cv2 matplotlib.pyplot | 1 | mgrid |
| lesson10_convolution | cv2 matplotlib.pyplot | 2 | convolve random.default_rng |
| lesson11_smoothing_pyramids | cv2 matplotlib.pyplot | 1 | random.default_rng |
| lesson12_differentiation_edges | cv2 matplotlib.pyplot | 3 | add.at random.default_rng unravel_index |
| lesson13_laplacian_pyramids | cv2 matplotlib.pyplot | 3 | corrcoef gradient random.default_rng |
| lesson14_nonlinear_filters | cv2 matplotlib.pyplot | 2 | mgrid random.default_rng |
| lesson15_fourier_frequency_filtering | cv2 matplotlib.pyplot | 8 | fft.fft2 fft.fftshift fft.ifft2 fft.ifftshift mgrid random.default_rng real unravel_index |
| lesson16_wavelets_gabor | cv2 matplotlib.pyplot pywt | 0 |  |
| lesson17_compression | cv2 matplotlib.pyplot | 2 | diff random.default_rng |
| lesson18_color_spaces | cv2 matplotlib.pyplot | 4 | corrcoef median mgrid random.default_rng |
| lesson19_clustering | cv2 matplotlib.pyplot | 7 | bincount cov linalg.det linalg.inv mgrid random.default_rng unique |
| lesson20_feature_detection_matching | cv2 matplotlib.pyplot | 1 | corrcoef |
| lesson21_optical_flow | cv2 matplotlib.pyplot | 2 | linalg.lstsq random.default_rng |
| lesson22_stereo_matching | cv2 matplotlib.pyplot | 3 | mgrid random.default_rng s_ |
| lesson23_model_fitting_ransac | cv2 matplotlib.pyplot | 4 | cov linalg.eigh linalg.lstsq random.default_rng |
| lesson24_projective_geometry | cv2 matplotlib.pyplot | 3 | cross linalg.solve random.default_rng |
| lesson25_stitching_mosaicking | cv2 matplotlib.pyplot | 0 |  |
| lesson26_image_formation | cv2 matplotlib.pyplot | 1 | random.default_rng |
| lesson27_camera_calibration | cv2 matplotlib.pyplot | 2 | mgrid random.default_rng |
| lesson28_epipolar_geometry | cv2 matplotlib.pyplot mpl_toolkits.mplot3d.art3d | 2 | linalg.svd random.default_rng |
| lesson29_structure_from_motion | cv2 matplotlib.pyplot mpl_toolkits.mplot3d | 1 | linalg.svd |
| lesson30_projection | matplotlib.pyplot | 5 | convolve cov fft.fft linalg.eigh random.default_rng |
| lesson31_neural_network_fundamentals | matplotlib.pyplot torch | 1 | random.default_rng |
| lesson32_multilayer_perceptrons | matplotlib.pyplot torch | 3 | cov linalg.eigh random.default_rng |
| lesson33_optimization | matplotlib.pyplot torch torch.nn | 1 | random.default_rng |
| lesson34_convolutional_neural_networks | matplotlib.pyplot torch torch.nn torch.nn.functional | 2 | mgrid random.default_rng |
| lesson35_training_a_cnn | cv2 matplotlib.pyplot tarfile torch torch.nn torch.nn.functional | 2 | mgrid random.default_rng |
| lesson36_image_classification_practice | matplotlib.pyplot tarfile torch torch.nn torch.nn.functional | 2 | bincount random.default_rng |
| lesson37_classic_architectures | matplotlib.pyplot torch torch.nn | 0 |  |
| lesson38_transfer_learning | matplotlib.pyplot tarfile torch torch.nn torch.nn.functional torchvision | 1 | random.default_rng |
| lesson39_visualizing_cnns | matplotlib.pyplot tarfile torch torch.nn torch.nn.functional | 2 | random.default_rng unravel_index |
| lesson40_object_detection_sliding_windows | cv2 matplotlib.patches matplotlib.pyplot torch torch.nn torch.nn.functional | 3 | mgrid percentile random.default_rng |
| lesson41_object_detection_regression | cv2 matplotlib.patches matplotlib.pyplot torch torch.nn torch.nn.functional torchvision | 2 | mgrid random.default_rng |
| lesson42_semantic_segmentation | cv2 matplotlib.patches matplotlib.pyplot torch torch.nn torch.nn.functional torchvision | 4 | bincount mgrid random.default_rng unique |
| lesson43_instance_segmentation | cv2 matplotlib.patches matplotlib.pyplot torch torch.nn torch.nn.functional torchvision | 3 | mgrid random.default_rng unravel_index |
| lesson44_precision_and_parallel_training | matplotlib.pyplot torch torch.nn torch.nn.functional | 0 |  |
| lesson45_attention_mechanism | matplotlib.pyplot torch torch.nn torch.nn.functional | 0 |  |
| lesson46_transformer_architecture | matplotlib.pyplot torch torch.nn torch.nn.functional | 1 | random.default_rng |
| lesson47_vision_transformers | matplotlib.pyplot torch torch.nn torch.nn.functional | 2 | mgrid random.default_rng |
| lesson48_autoencoders | matplotlib.pyplot torch torch.nn torch.nn.functional | 3 | linalg.svd mgrid random.default_rng |
| lesson49_self_supervised_learning | cv2 matplotlib.pyplot torch torch.nn torch.nn.functional | 2 | mgrid random.default_rng |
| lesson50_dino_self_distillation | cv2 matplotlib.pyplot torch torch.nn torch.nn.functional | 2 | mgrid random.default_rng |
| lesson51_masked_autoencoders | matplotlib.pyplot torch torch.nn torch.nn.functional | 2 | mgrid random.default_rng |
| lesson52_vision_language_models | matplotlib.pyplot torch torch.nn torch.nn.functional | 2 | mgrid random.default_rng |
| lesson53_visual_question_answering | matplotlib.pyplot torch torch.nn torch.nn.functional | 3 | bincount mgrid random.default_rng |
| lesson54_segment_anything | matplotlib.pyplot torch torch.nn torch.nn.functional | 2 | mgrid random.default_rng |
| lesson55_open_vocabulary_detection | matplotlib.patches matplotlib.pyplot torch torch.nn torch.nn.functional | 2 | mgrid random.default_rng |
| lesson56_monocular_depth_estimation | matplotlib.pyplot torch torch.nn torch.nn.functional | 2 | mgrid random.default_rng |
| lesson57_depth_anything | matplotlib.pyplot torch torch.nn torch.nn.functional | 2 | mgrid random.default_rng |
| lesson58_stereo_foundationstereo | cv2 matplotlib.pyplot torch torch.nn torch.nn.functional | 1 | random.default_rng |
| lesson59_feedforward_pose_estimation | cv2 matplotlib.pyplot torch torch.nn torch.nn.functional | 2 | linalg.inv random.default_rng |
| lesson60_diffusion_models | cv2 matplotlib.pyplot torch torch.nn torch.nn.functional | 2 | mgrid random.default_rng |
| lesson61_neural_rendering | matplotlib.pyplot torch torch.nn torch.nn.functional | 2 | cross random.default_rng |

**Fully importable and numpy-complete: 0 / 61**

