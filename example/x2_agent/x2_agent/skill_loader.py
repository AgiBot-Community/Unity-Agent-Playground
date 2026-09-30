"""技能配置加载和验证。"""
import os
import math
import yaml
from typing import Dict, List, Any, Optional


class SkillLoader:
    """从 YAML 文件加载技能配置。"""

    def __init__(self, config_path: Optional[str] = None):
        """初始化技能加载器。

        Args:
            config_path: 技能配置文件路径，默认为同目录下的 skills.yaml
        """
        if config_path is None:
            config_path = os.path.join(
                os.path.dirname(__file__),
                "..",
                "skills.yaml"
            )
        self.config_path = config_path
        self._config: Optional[Dict[str, Any]] = None

    def load(self) -> Dict[str, Any]:
        """加载技能配置。

        Returns:
            技能配置字典

        Raises:
            FileNotFoundError: 配置文件不存在
            yaml.YAMLError: 配置文件格式错误
        """
        if self._config is not None:
            return self._config

        with open(self.config_path, 'r', encoding='utf-8') as f:
            config = yaml.safe_load(f)
        self._validate(config)
        self._config = config
        return self._config

    @staticmethod
    def _validate(config) -> None:
        """验证配置格式。"""
        if not isinstance(config, dict):
            raise ValueError("技能配置必须是字典格式")
        if config.get("version") != "1.0":
            raise ValueError("技能配置 version 必须为 '1.0'")
        categories = config.get("skill_categories")
        if not isinstance(categories, dict) or not categories:
            raise ValueError("skill_categories 必须为非空字典")
        for category, definition in categories.items():
            if not isinstance(category, str) or not category or not isinstance(definition, dict):
                raise ValueError("无效的技能分类")
            skills = definition.get("skills")
            if not isinstance(skills, list) or not skills:
                raise ValueError(f"{category}.skills 必须为非空列表")
            names = set()
            for skill in skills:
                if not isinstance(skill, dict) or not isinstance(skill.get("name"), str):
                    raise ValueError("技能必须包含 name")
                if not skill["name"] or skill["name"] in names:
                    raise ValueError("技能名称为空或重复")
                names.add(skill["name"])
                parameters = skill.get("parameters", [])
                if not isinstance(parameters, list):
                    raise ValueError("parameters 必须为列表")
                param_names = set()
                for param in parameters:
                    if (not isinstance(param, dict) or not isinstance(param.get("name"), str)
                            or not param["name"] or param["name"] in param_names
                            or param.get("type") not in ("float", "int")):
                        raise ValueError("无效或重复的技能参数")
                    param_names.add(param["name"])
                    for key in ("min", "max", "default"):
                        if key in param:
                            value = param[key]
                            if (type(value) not in (int, float) or not math.isfinite(value)
                                    or (param["type"] == "int" and value != int(value))):
                                raise ValueError(f"{param['name']}.{key} 必须为有限数值")
                    if param.get("min", -math.inf) > param.get("max", math.inf):
                        raise ValueError("参数 min 不能大于 max")
                    if "default" in param and not (
                            param.get("min", -math.inf) <= param["default"] <= param.get("max", math.inf)):
                        raise ValueError("参数 default 必须在范围内")
        acks = config.get("acknowledgments", {})
        if not isinstance(acks, dict) or any(
                not isinstance(key, str) or not isinstance(value, str)
                for key, value in acks.items()):
            raise ValueError("acknowledgments 必须为字符串字典")

    def get_tool_definition(self) -> List[Dict[str, Any]]:
        """转换为 LLM function calling 工具定义。

        Returns:
            LLM 工具定义列表
        """
        config = self.load()
        categories = config["skill_categories"]

        # 收集所有技能类型和名称
        skill_types = []
        skill_names = []
        properties = {}
        descriptions = []

        for category_name, category_data in categories.items():
            skill_types.append(category_name)
            for skill in category_data.get("skills", []):
                skill_names.append(skill["name"])
                descriptions.append(f"{category_name}/{skill['name']}")
                for param in skill.get("parameters", []):
                    schema = {"type": "integer" if param["type"] == "int" else "number"}
                    for source, target in (("min", "minimum"), ("max", "maximum"),
                                           ("default", "default"), ("description", "description")):
                        if source in param:
                            schema[target] = param[source]
                    name = param["name"]
                    if name in properties and properties[name] != schema:
                        raise ValueError(f"共享参数 {name} 的定义不一致")
                    properties[name] = schema

        # 构建工具定义
        return [{
            "type": "function",
            "function": {
                "name": "robot_skill",
                "description": (
                    "让机器人执行一个动作或表情技能。用户表达动作/表情/移动意图时调用；"
                    "调用后正常用自然语言回应即可。有效类型/名称组合："
                    + "、".join(descriptions)
                ),
                "parameters": {
                    "type": "object",
                    "properties": {
                        "skillType": {
                            "type": "string",
                            "enum": skill_types
                        },
                        "skillName": {
                            "type": "string",
                            "enum": skill_names
                        },
                        "skillParam": {
                            "type": "object",
                            "properties": properties,
                        },
                    },
                    "required": ["skillType", "skillName"],
                },
            },
        }]

    def get_acknowledgment(self, skill_type: str, skill_name: str,
                          skill_param: Optional[Dict[str, Any]] = None) -> str:
        """获取技能的即时口播。

        Args:
            skill_type: 技能类型
            skill_name: 技能名称
            skill_param: 技能参数

        Returns:
            口播文本
        """
        config = self.load()
        acks = config.get("acknowledgments", {})

        key = f"{skill_type}/{skill_name}"
        if key in acks:
            return acks[key]
        # 默认移动口播保留参数；显式配置的口播优先。
        if skill_type == "movement" and skill_name == "walk":
            try:
                distance = float((skill_param or {}).get("distanceM", 1))
                return f"好，我往前走{distance:g}米。"
            except (TypeError, ValueError):
                pass
        elif skill_type == "movement" and skill_name == "turn":
            try:
                angle = float((skill_param or {}).get("angleDeg", 90))
                direction = "右" if angle >= 0 else "左"
                return f"好，我向{direction}转。"
            except (TypeError, ValueError):
                pass

        return acks.get("default", "好的，我这就来！")

    def validate_request(self, skill_type, skill_name, parameters):
        """Reject unknown pairs/invalid numbers; clamp only finite numeric bounds."""
        if not isinstance(skill_type, str) or not isinstance(skill_name, str):
            raise ValueError("技能类型和名称必须为字符串")
        skill = self.get_skill_info(skill_type, skill_name)
        if skill is None:
            raise ValueError(f"未知技能 {skill_type}/{skill_name}")
        if parameters is None:
            parameters = {}
        if not isinstance(parameters, dict):
            raise ValueError("skillParam 必须为对象")
        definitions = {param["name"]: param for param in skill.get("parameters", [])}
        if parameters.keys() - definitions.keys():
            raise ValueError("未知技能参数")
        result = {}
        for name, param in definitions.items():
            if name not in parameters and "default" not in param:
                continue
            value = parameters.get(name, param.get("default"))
            if (type(value) not in (int, float) or not math.isfinite(value)
                    or (param["type"] == "int" and value != int(value))):
                raise ValueError(f"{name} 必须为有限数值")
            result[name] = min(param.get("max", math.inf), max(param.get("min", -math.inf), value))
        return result

    def get_skill_info(self, skill_type: str, skill_name: str) -> Optional[Dict[str, Any]]:
        """获取技能详细信息。

        Args:
            skill_type: 技能类型
            skill_name: 技能名称

        Returns:
            技能信息字典，如果不存在返回 None
        """
        config = self.load()
        categories = config.get("skill_categories", {})

        if skill_type not in categories:
            return None

        for skill in categories[skill_type].get("skills", []):
            if skill["name"] == skill_name:
                return skill

        return None
