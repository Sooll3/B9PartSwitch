﻿using System;
using System.Collections.Generic;
using UniLinq;
using UnityEngine;
using B9PartSwitch.Fishbones;
using B9PartSwitch.Fishbones.Context;
using B9PartSwitch.PartSwitch.PartModifiers;

namespace B9PartSwitch
{
    public class PartSubtype : IContextualNode
    {
        #region Config Fields

        [NodeData(name = "name")]
        public string subtypeName;

        [NodeData]
        public string title;

        [NodeData(name = "transform")]
        public List<string> transformNames = new List<string>();

        [NodeData(name = "node")]
        public List<string> nodeNames = new List<string>();

        [NodeData(name = "TEXTURE")]
        public List<TextureSwitchInfo> textureSwitches = new List<TextureSwitchInfo>();

        [NodeData(name = "NODE")]
        public List<AttachNodeModifierInfo> attachNodeModifierInfos = new List<AttachNodeModifierInfo>();

        [NodeData]
        public float addedMass = 0f;

        [NodeData]
        public float addedCost = 0f;

        [UseParser(typeof(TankTypeValueParser))]
        [NodeData]
        public TankType tankType;

        [NodeData]
        public float volumeMultiplier = 1f;

        [NodeData]
        public float volumeAdded = 0f;

        [NodeData]
        public float volumeAddedToParent = 0f;

        [NodeData]
        public float? percentFilled;

        [NodeData]
        public bool? resourcesTweakable;

        [NodeData]
        public float maxTemp;

        [NodeData]
        public float skinMaxTemp;

        [NodeData]
        public AttachNode attachNode = null;

        [NodeData]
        public float crashTolerance = 0f;

        [NodeData]
        public Vector3 CoMOffset = Vector3Extensions.NaN();

        [NodeData]
        public Vector3 CoPOffset = Vector3Extensions.NaN();

        [NodeData]
        public Vector3 CoLOffset = Vector3Extensions.NaN();

        [NodeData]
        public Vector3 CenterOfBuoyancy = Vector3Extensions.NaN();

        [NodeData]
        public Vector3 CenterOfDisplacement = Vector3Extensions.NaN();

        [NodeData]
        public int stackSymmetry = -1;

        [NodeData]
        public bool allowSwitchInFlight = true;

        #endregion

        #region Private Fields

        private ModuleB9PartSwitch parent;
        private List<Transform> transforms = new List<Transform>();
        private List<AttachNode> nodes = new List<AttachNode>();
        private List<IPartModifier> partModifiers = new List<IPartModifier>();
        private List<object> aspectLocks = new List<object>();

        #endregion

        #region Properties

        public string Name => subtypeName;

        public Part Part => parent.part;

        public bool HasTank => tankType != null && tankType.ResourcesCount > 0;

        public IEnumerable<Transform> Transforms => transforms.Select(transform => transform.transform);
        public IEnumerable<AttachNode> Nodes => nodes.All();
        public IEnumerable<string> ResourceNames => tankType.ResourceNames;
        public IEnumerable<string> NodeIDs => nodes.Select(n => n.id);

        public float TotalVolume
        {
            get
            {
                if (parent.IsNull()) throw new InvalidOperationException("Cannot get volume before parent has been linked!");

                if (!HasTank) return 0f;
                return (parent.baseVolume * volumeMultiplier + volumeAdded + parent.VolumeFromChildren) * parent.VolumeScale;
            }
        }

        public float TotalMass => TotalVolume * tankType.tankMass + addedMass * parent.VolumeScale;
        public float TotalCost => TotalVolume * tankType.TotalUnitCost + addedCost * parent.VolumeScale;

        public bool ChangesMass => (addedMass != 0f) || tankType.ChangesMass;
        public bool ChangesCost => (addedCost != 0f) || tankType.ChangesCost;

        public IEnumerable<object> PartAspectLocks => aspectLocks.All();

        #endregion

        #region Interface Methods

        public void Load(ConfigNode node, OperationContext context)
        {
            OperationContext newContext;

            try
            {
                newContext = this.LoadFields(node, context);
            }
            catch (Exception ex)
            {
                throw new Exception($"Exception while loading fields on subtype {this}", ex);
            }

            OnLoad(node, newContext);
        }

        public void Save(ConfigNode node, OperationContext context)
        {
            try
            {
                this.SaveFields(node, context);
            }
            catch (Exception ex)
            {
                throw new Exception($"Exception while loading fields on subtype {this}", ex);
            }
        }

        #endregion

        #region Setup

        private void OnLoad(ConfigNode node, OperationContext context)
        {
            if (Name.IsNullOrEmpty())
            {
                SeriousWarningHandler.DisplaySeriousWarning($"Subtype has no name: {this}");
                LogError("Subtype has no name");
            }

            if (tankType == null)
                tankType = B9TankSettings.StructuralTankType;

            if (context.Operation == Operation.LoadPrefab)
            {
                if (title.IsNullOrEmpty())
                    title = subtypeName;

                ConfigNode[] resourceNodes = node.GetNodes("RESOURCE");

                if (resourceNodes.Length > 0)
                {
                    LoadAdditionalResources(resourceNodes, context);
                }
            }
        }

        public void Setup(ModuleB9PartSwitch parent)
        {
            if (parent == null)
                throw new ArgumentNullException("parent cannot be null");
            if (parent.part == null)
                throw new ArgumentNullException("parent.part cannot be null");

            this.parent = parent;

            aspectLocks.Clear();

            FindObjects();

            Part partPrefab = Part.GetPrefab() ?? Part;

            partModifiers.ForEach(modifier => modifier.OnBeforeReinitialize());
            partModifiers.Clear();

            IEnumerable<object> aspectLocksOnOtherModules = parent.PartAspectLocksOnOtherModules;

            void MaybeAddModifier(IPartModifier modifier)
            {
                if (modifier == null) return;
                if (aspectLocksOnOtherModules.Contains(modifier.PartAspectLock))
                {
                    LogError($"More than one module can't manage {modifier.Description}");
                }
                else
                {
                    partModifiers.Add(modifier);
                    aspectLocks.Add(modifier.PartAspectLock);
                }
            }

            if (maxTemp > 0)
                MaybeAddModifier(new PartMaxTempModifier(Part, partPrefab.maxTemp, maxTemp));

            if (skinMaxTemp > 0)
                MaybeAddModifier(new PartSkinMaxTempModifier(Part, partPrefab.skinMaxTemp, skinMaxTemp));

            if (crashTolerance > 0)
                MaybeAddModifier(new PartCrashToleranceModifier(Part, partPrefab.crashTolerance, crashTolerance));

            if (attachNode.IsNotNull())
            {
                if (Part.attachRules.allowSrfAttach)
                {
                    if (Part.srfAttachNode.IsNotNull())
                        MaybeAddModifier(new PartAttachNodeModifier(Part.srfAttachNode, partPrefab.srfAttachNode, attachNode, parent));
                    else
                        LogError("attachNode specified but part does not have a surface attach node");
                }
                else
                {
                    LogError("attachNode specified but part does not allow surface attach");
                }
            }

            if (CoMOffset.IsFinite())
                MaybeAddModifier(new PartCoMOffsetModifier(Part, partPrefab.CoMOffset, CoMOffset));

            if (CoPOffset.IsFinite())
                MaybeAddModifier(new PartCoPOffsetModifier(Part, partPrefab.CoPOffset, CoPOffset));

            if (CoLOffset.IsFinite())
                MaybeAddModifier(new PartCoLOffsetModifier(Part, partPrefab.CoLOffset, CoLOffset));

            if (CenterOfBuoyancy.IsFinite())
                MaybeAddModifier(new PartCenterOfBuoyancyModifier(Part, partPrefab.CenterOfBuoyancy, CenterOfBuoyancy));

            if (CenterOfDisplacement.IsFinite())
                MaybeAddModifier(new PartCenterOfDisplacementModifier(Part, partPrefab.CenterOfDisplacement, CenterOfDisplacement));

            if (stackSymmetry >= 0)
                MaybeAddModifier(new PartStackSymmetryModifier(Part, partPrefab.stackSymmetry, stackSymmetry));

            foreach (AttachNodeModifierInfo info in attachNodeModifierInfos)
            {
                MaybeAddModifier(info.CreateAttachNodeModifier(Part, parent));
            }

            foreach (TextureSwitchInfo info in textureSwitches)
            {
                foreach(TextureReplacement replacement in info.CreateTextureReplacements(Part))
                {
                    MaybeAddModifier(replacement);
                }
            }

            nodes = new List<AttachNode>();
            foreach (string nodeName in nodeNames)
            {
                bool foundNode = false;

                foreach (AttachNode node in Part.attachNodes.Where(node => node.id == nodeName))
                {
                    foundNode = true;

                    if (node.nodeType != AttachNode.NodeType.Stack)
                    {
                        LogError($"Node {node.id} is not a stack node, and thus cannot be managed by ModuleB9PartSwitch");
                        continue;
                    }

                    nodes.Add(node);
                    partModifiers.Add(new AttachNodeToggler(node));
                }

                if (!foundNode) LogError($"No attach nodes matching '{nodeName}' found");
            }
        }

        #endregion

        #region Public Methods

        public void DeactivateOnStart()
        {
            DeactivateObjects();

            if (HighLogic.LoadedSceneIsEditor)
                partModifiers.ForEach(modifier => modifier.DeactivateOnStartEditor());
            else
                partModifiers.ForEach(modifier => modifier.DeactivateOnStartFlight());
        }

        public void ActivateOnStart()
        {
            ActivateObjects();
            AddResources(false);

            if (HighLogic.LoadedSceneIsEditor)
                partModifiers.ForEach(modifier => modifier.ActivateOnStartEditor());
            else
                partModifiers.ForEach(modifier => modifier.ActivateOnStartFlight());
        }

        public void ActivateAfterStart()
        {
            partModifiers.ForEach(modifier => modifier.ActivateAfterStart());
        }

        public void DeactivateOnSwitch()
        {
            DeactivateObjects();
            RemoveResources();

            if (HighLogic.LoadedSceneIsEditor)
                partModifiers.ForEach(modifier => modifier.DeactivateOnSwitchEditor());
            else
                partModifiers.ForEach(modifier => modifier.DeactivateOnSwitchFlight());
        }

        public void ActivateOnSwitch()
        {
            ActivateObjects();
            AddResources(true);

            if (HighLogic.LoadedSceneIsEditor)
                partModifiers.ForEach(modifier => modifier.ActivateOnSwitchEditor());
            else
                partModifiers.ForEach(modifier => modifier.ActivateOnSwitchFlight());
        }

        public void DeactivateForIcon()
        {
            DeactivateObjects();

            partModifiers.ForEach(modifier => modifier.OnIconCreateInactiveSubtype());
        }

        public void ActivateForIcon()
        {
            ActivateObjects();

            partModifiers.ForEach(modifier => modifier.OnIconCreateActiveSubtype());
        }

        public void UpdateVolume()
        {
            AddResources(true);

            if (HighLogic.LoadedSceneIsEditor)
                partModifiers.ForEach(modifier => modifier.UpdateVolumeEditor());
            else
                partModifiers.ForEach(modifier => modifier.UpdateVolumeFlight());
        }

        public void OnWillBeCopiedActiveSubtype()
        {
            partModifiers.ForEach(modifier => modifier.OnWillBeCopiedActiveSubtype());
        }

        public void OnWillBeCopiedInactiveSubtype()
        {
            partModifiers.ForEach(modifier => modifier.OnWillBeCopiedInactiveSubtype());
        }

        public void OnWasCopiedActiveSubtype()
        {
            partModifiers.ForEach(modifier => modifier.OnWasCopiedActiveSubtype());
        }

        public void OnWasCopiedInactiveSubtype()
        {
            partModifiers.ForEach(modifier => modifier.OnWasCopiedInactiveSubtype());
        }

        public bool TransformIsManaged(Transform transform) => transforms.Contains(transform);
        public bool NodeManaged(AttachNode node) => nodes.Contains(node);
        public bool ResourceManaged(String resourceName) => ResourceNames.Contains(resourceName);

        public void AssignStructuralTankType()
        {
            if (!tankType.IsStructuralTankType)
                tankType = B9TankSettings.StructuralTankType;
        }

        public override string ToString()
        {
            string log = "PartSubtype";
            if (!Name.IsNullOrEmpty())
                log += $" {Name}";
            if (parent != null)
                log += $" on module {parent}";
            return log;
        }

        #endregion

        #region Private Methods

        private void LoadAdditionalResources(ConfigNode[] resourceNodes, OperationContext context)
        {
            OperationContext newContext = new OperationContext(context, this);
            foreach (ConfigNode resourceNode in resourceNodes)
            {
                string name = resourceNode.GetValue("name");

                if (name.IsNullOrEmpty())
                {
                    LogError("Cannot load a RESOURCE node without a name");
                    continue;
                }

                TankResource resource = tankType[name];

                if (resource.IsNull())
                {
                    resource = new TankResource();
                    tankType.resources.Add(resource);
                }

                resource.Load(resourceNode, newContext);
            }
        }

        private void FindObjects()
        {
            if (parent == null)
                throw new InvalidOperationException("Parent has not been set");

            transforms.Clear();
            foreach (var transformName in transformNames)
            {
                Transform[] tempTransforms = Part.FindModelTransforms(transformName);
                if (tempTransforms == null || tempTransforms.Length == 0)
                    LogError($"No transforms named {transformName} found");
                else
                    transforms.AddRange(tempTransforms);
            }
        }

        private void ActivateObjects() => transforms.ForEach(t => Part.UpdateTransformEnabled(t));
        private void DeactivateObjects() => transforms.ForEach(t => t.Disable());

        private void AddResources(bool fillTanks)
        {
            foreach (TankResource resource in tankType.resources)
            {
                float amount = TotalVolume * resource.unitsPerVolume;
                float filledProportion;
                if (HighLogic.LoadedSceneIsFlight && fillTanks)
                    filledProportion = 0;
                else
                    filledProportion = (resource.percentFilled ?? percentFilled ?? tankType.percentFilled ?? 100f) * 0.01f;
                PartResource partResource = Part.AddOrCreateResource(resource.resourceDefinition, amount, amount * filledProportion, fillTanks);

                bool? tweakable = resourcesTweakable ?? tankType.resourcesTweakable;

                if (tweakable.HasValue)
                    partResource.isTweakable = tweakable.Value;
            }
        }

        private void RemoveResources()
        {
            foreach (TankResource resource in tankType.resources)
            {
                Part.RemoveResource(resource.ResourceName);
            }
        }

        #region Logging

        private void LogWarning(string message) => Debug.LogWarning($"Warning on {this}: {message}");

        private void LogError(string message) => Debug.LogWarning($"Warning on {this}: {message}");

        #endregion

        #endregion
    }
}
