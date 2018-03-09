﻿' Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

Imports Microsoft.VisualStudio.LanguageServices.Implementation.CodeModel
Imports Microsoft.VisualStudio.LanguageServices.Implementation.ProjectSystem

Namespace Microsoft.VisualStudio.LanguageServices.VisualBasic.ProjectSystemShim
    Partial Friend Class VisualBasicProjectShimWithServices
        Implements IProjectCodeModelProvider

        Private _projectCodeModel As ProjectCodeModel

        Public ReadOnly Property ProjectCodeModel As ProjectCodeModel Implements IProjectCodeModelProvider.ProjectCodeModel
            Get
                LazyInitialization.EnsureInitialized(_projectCodeModel, Function() New ProjectCodeModel(Me.Id, New VisualBasicCodeModelInstanceFactory(Me), DirectCast(Me.Workspace, VisualStudioWorkspaceImpl), ServiceProvider))
                Return _projectCodeModel
            End Get
        End Property

        Public Overrides Function CreateCodeModel(pProject As EnvDTE.Project, pProjectItem As EnvDTE.ProjectItem, ByRef ppCodeModel As EnvDTE.CodeModel) As Integer
            ppCodeModel = ProjectCodeModel.GetOrCreateRootCodeModel(pProject)

            Return VSConstants.S_OK
        End Function

        Public Overrides Function CreateFileCodeModel(pProject As EnvDTE.Project, pProjectItem As EnvDTE.ProjectItem, ByRef ppFileCodeModel As EnvDTE.FileCodeModel) As Integer
            ppFileCodeModel = Nothing

            If pProjectItem IsNot Nothing Then
                Dim fileName = pProjectItem.FileNames(1)

                If Not String.IsNullOrWhiteSpace(fileName) Then
                    ppFileCodeModel = ProjectCodeModel.GetOrCreateFileCodeModel(fileName, pProjectItem).Handle
                    Return VSConstants.S_OK
                End If
            End If

            Return VSConstants.E_INVALIDARG
        End Function

        Protected Overrides Sub OnDocumentRemoved(filePath As String)
            MyBase.OnDocumentRemoved(filePath)

            ' We may have a code model floating around for it
            ProjectCodeModel.OnSourceFileRemoved(filePath)
        End Sub

        Public Overrides Sub Disconnect()
            ' Clear code model cache and shutdown instances, if any exists.
            _projectCodeModel?.OnProjectClosed()

            MyBase.Disconnect()
        End Sub
    End Class
End Namespace
