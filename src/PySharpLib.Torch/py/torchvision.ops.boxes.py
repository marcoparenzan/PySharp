# Port of torchvision.ops.boxes: the same Python code, with nms backed by PySharp.Torch's native kernel instead of torch.ops.torchvision.nms.
import torch
from torch import Tensor
from torchvision.ops import _box_convert
from torchvision.utils import _log_api_usage_once


def nms(boxes, scores, iou_threshold):
    return torch._C._vision_nms(boxes, scores, iou_threshold)


def batched_nms(boxes, scores, idxs, iou_threshold):
    if boxes.numel() > 4000:
        return _batched_nms_vanilla(boxes, scores, idxs, iou_threshold)
    return _batched_nms_coordinate_trick(boxes, scores, idxs, iou_threshold)


def _batched_nms_coordinate_trick(boxes, scores, idxs, iou_threshold):
    if boxes.numel() == 0:
        return torch.empty((0,), dtype=torch.int64)
    max_coordinate = boxes.max()
    offsets = idxs.to(boxes) * (max_coordinate + torch.tensor(1).to(boxes))
    boxes_for_nms = boxes + offsets[:, None]
    keep = nms(boxes_for_nms, scores, iou_threshold)
    return keep


def _batched_nms_vanilla(boxes, scores, idxs, iou_threshold):
    keep_mask = torch.zeros_like(scores, dtype=torch.bool)
    for class_id in torch.unique(idxs):
        curr_indices = torch.where(idxs == class_id)[0]
        curr_keep_indices = nms(boxes[curr_indices], scores[curr_indices], iou_threshold)
        keep_mask[curr_indices[curr_keep_indices]] = True
    keep_indices = torch.where(keep_mask)[0]
    return keep_indices[scores[keep_indices].sort(descending=True)[1]]


def remove_small_boxes(boxes, min_size):
    ws, hs = boxes[:, 2] - boxes[:, 0], boxes[:, 3] - boxes[:, 1]
    keep = (ws >= min_size) & (hs >= min_size)
    keep = torch.where(keep)[0]
    return keep


def clip_boxes_to_image(boxes, size):
    dim = boxes.dim()
    boxes_x = boxes[..., 0::2]
    boxes_y = boxes[..., 1::2]
    height, width = size
    boxes_x = boxes_x.clamp(min=0, max=width)
    boxes_y = boxes_y.clamp(min=0, max=height)
    clipped_boxes = torch.stack((boxes_x, boxes_y), dim=dim)
    return clipped_boxes.reshape(boxes.shape)


def box_convert(boxes, in_fmt, out_fmt):
    allowed_fmts = ("xyxy", "xywh", "cxcywh")
    if in_fmt not in allowed_fmts or out_fmt not in allowed_fmts:
        raise ValueError("Unsupported Bounding Box Conversions for given in_fmt and out_fmt")
    if in_fmt == out_fmt:
        return boxes.clone()
    if in_fmt != "xyxy" and out_fmt != "xyxy":
        if in_fmt == "xywh":
            boxes = _box_convert._box_xywh_to_xyxy(boxes)
        elif in_fmt == "cxcywh":
            boxes = _box_convert._box_cxcywh_to_xyxy(boxes)
        in_fmt = "xyxy"
    if in_fmt == "xyxy":
        if out_fmt == "xywh":
            boxes = _box_convert._box_xyxy_to_xywh(boxes)
        elif out_fmt == "cxcywh":
            boxes = _box_convert._box_xyxy_to_cxcywh(boxes)
    elif out_fmt == "xyxy":
        if in_fmt == "xywh":
            boxes = _box_convert._box_xywh_to_xyxy(boxes)
        elif in_fmt == "cxcywh":
            boxes = _box_convert._box_cxcywh_to_xyxy(boxes)
    return boxes


def _upcast(t):
    if t.is_floating_point():
        return t if t.dtype in (torch.float32, torch.float64) else t.float()
    return t if t.dtype in (torch.int32, torch.int64) else t.int()


def box_area(boxes):
    boxes = _upcast(boxes)
    return (boxes[:, 2] - boxes[:, 0]) * (boxes[:, 3] - boxes[:, 1])


def _box_inter_union(boxes1, boxes2):
    area1 = box_area(boxes1)
    area2 = box_area(boxes2)
    lt = torch.max(boxes1[:, None, :2], boxes2[:, :2])
    rb = torch.min(boxes1[:, None, 2:], boxes2[:, 2:])
    wh = _upcast(rb - lt).clamp(min=0)
    inter = wh[:, :, 0] * wh[:, :, 1]
    union = area1[:, None] + area2 - inter
    return inter, union


def box_iou(boxes1, boxes2):
    inter, union = _box_inter_union(boxes1, boxes2)
    iou = inter / union
    return iou
