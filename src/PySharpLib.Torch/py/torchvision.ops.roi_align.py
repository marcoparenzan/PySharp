import torch
from torch import nn, Tensor
from torchvision.ops._utils import check_roi_boxes_shape, convert_boxes_to_roi_format


def _pair(v):
    if isinstance(v, (tuple, list)):
        return tuple(v)
    return (v, v)


def roi_align(input, boxes, output_size, spatial_scale=1.0, sampling_ratio=-1, aligned=False):
    check_roi_boxes_shape(boxes)
    rois = boxes
    output_size = _pair(output_size)
    if not isinstance(rois, torch.Tensor):
        rois = convert_boxes_to_roi_format(rois)
    return torch._C._vision_roi_align(input, rois, spatial_scale, output_size[0], output_size[1], sampling_ratio, aligned)


class RoIAlign(nn.Module):
    def __init__(self, output_size, spatial_scale, sampling_ratio, aligned=False):
        super().__init__()
        self.output_size = output_size
        self.spatial_scale = spatial_scale
        self.sampling_ratio = sampling_ratio
        self.aligned = aligned

    def forward(self, input, rois):
        return roi_align(input, rois, self.output_size, self.spatial_scale, self.sampling_ratio, self.aligned)
