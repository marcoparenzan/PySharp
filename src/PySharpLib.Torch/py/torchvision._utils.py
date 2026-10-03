import enum


class StrEnum(enum.Enum):
    @classmethod
    def from_str(cls, member_str):
        for member in cls:
            if member.name == member_str.upper() or member.name == member_str:
                return member
        raise ValueError("Unknown value '" + member_str + "' for " + cls.__name__ + ".")


def sequence_to_str(seq, separate_last=""):
    if not seq:
        return ""
    if len(seq) == 1:
        return "'" + str(seq[0]) + "'"
    head = "'" + "', '".join([str(item) for item in seq[:-1]]) + "'"
    tail = ("" if separate_last and len(seq) == 2 else ",") + " " + separate_last + "'" + str(seq[-1]) + "'"
    return head + tail
