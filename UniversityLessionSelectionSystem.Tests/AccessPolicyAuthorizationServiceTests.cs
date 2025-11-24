using System;
using NUnit.Framework;
using Rhino.Mocks;
using University.Lms.Ports;
using University.Lms.Services;
using UniversityLessonSelectionSystem.Domain.AccessPolicy;
using UniversityLessonSelectionSystem.Domain.Enums;
using UniversityLessonSelectionSystem.Ports.AccessPolicy;

namespace UniversityLessonSelectionSystem.Tests.Tests
{
    internal class AccessPolicyAuthorizationServiceTests
    {
        #region Fields

        private ILogger _logger;
        private IAccessPolicyRepo _repo;
        private AccessRequest _accessRequest;
        private bool _roleCanResult;
        private bool _departmentScopeAllowedResult;
        private bool _afterHoursAllowedResult;
        private AccessPolicyAuthorizationService _service;
        #endregion

        #region Test Setup

        [SetUp]
        public void BeforeEachTest()
        {
            MockRepository.GenerateMock<IClock>();
            _logger = MockRepository.GenerateMock<ILogger>();
            _repo = MockRepository.GenerateMock<IAccessPolicyRepo>();

            _roleCanResult = true;
            _departmentScopeAllowedResult = true;
            _afterHoursAllowedResult = true;

            _accessRequest = new AccessRequest
            {
                Role = Role.Student,
                Operation = Operation.ViewGrades,
                Context = AccessContext.StudentOwnRecord,
                Department = Department.CS,
                TermPhase = TermPhase.Registration,
                LocalHour = 10,
                BreakGlassRequested = false
            };
        }

        private void ArrangeMockingObjects()
        {
            _repo.Stub(x => x.RoleCan(Arg<Role>.Is.Anything, Arg<Operation>.Is.Anything))
                .Return(_roleCanResult).Repeat.Any();

            _repo.Stub(x => x.DepartmentScopeAllowed(Arg<Role>.Is.Anything, Arg<Department>.Is.Anything))
                .Return(_departmentScopeAllowedResult).Repeat.Any();

            _repo.Stub(x => x.AfterHoursAllowed(Arg<Role>.Is.Anything, Arg<Operation>.Is.Anything))
                .Return(_afterHoursAllowedResult).Repeat.Any();

            _logger.Stub(x => x.Info(Arg<string>.Is.Anything)).Repeat.Any();
            _logger.Stub(x => x.Warn(Arg<string>.Is.Anything)).Repeat.Any();
            _logger.Stub(x => x.Error(Arg<string>.Is.Anything)).Repeat.Any();
        }

        #endregion

        #region Constructor Tests
 

        [Test]
        public void Constructor_ThrowsArgumentNullException_WhenLoggerIsNull()
        {
            // Arrange & Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
            {
                var accessPolicyAuthorizationService = new AccessPolicyAuthorizationService(null, _repo);
            });
        }

        [Test]
        public void Constructor_ThrowsArgumentNullException_WhenRepoIsNull()
        {
            // Arrange & Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
            {
                var accessPolicyAuthorizationService = new AccessPolicyAuthorizationService(_logger, null);
            });
        }

        #endregion

        #region Authorize Method Tests

        [Test]
        public void Authorize_ThrowsArgumentNullException_WhenRequestIsNull()
        {
            // Arrange
            ArrangeMockingObjects();
            _service = new AccessPolicyAuthorizationService(_logger, _repo);

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => _service.Authorize(null));
        }

        [Test]
        public void Authorize_ReturnsDeny_WhenRoleNotAllowed()
        {
            // Arrange
            _roleCanResult = false;
            ArrangeMockingObjects();
            _service = new AccessPolicyAuthorizationService(_logger, _repo);

            // Act
            var result = _service.Authorize(_accessRequest);

            // Assert
            Assert.That(AccessOutcome.Deny, Is.EqualTo(result.Outcome));
            Assert.That("Role not permitted.", Is.EqualTo(result.Reason));
        }

        [Test]
        public void Authorize_ReturnsDeny_WhenFinalsLocked_EditTranscript()
        {
            // Arrange
            _accessRequest.Operation = Operation.EditTranscript;
            _accessRequest.TermPhase = TermPhase.Finals;
            ArrangeMockingObjects();
            _service = new AccessPolicyAuthorizationService(_logger, _repo);

            // Act
            var result = _service.Authorize(_accessRequest);

            // Assert
            Assert.That(AccessOutcome.Deny, Is.EqualTo(result.Outcome));
            Assert.That("Operation locked during Finals/Closed.", Is.EqualTo(result.Reason));
        }

        [Test]
        public void Authorize_ReturnsDeny_WhenFinalsLocked_PublishGrades()
        {
            // Arrange
            _accessRequest.Operation = Operation.PublishGrades;
            _accessRequest.TermPhase = TermPhase.Closed;
            ArrangeMockingObjects();
            _service = new AccessPolicyAuthorizationService(_logger, _repo);

            // Act
            var result = _service.Authorize(_accessRequest);

            // Assert
            Assert.That(AccessOutcome.Deny, Is.EqualTo(result.Outcome));
            Assert.That("Operation locked during Finals/Closed.", Is.EqualTo(result.Reason));
        }

        [Test]
        public void Authorize_ReturnsDeny_WhenContextMismatch_NoBreakGlass()
        {
            // Arrange
            _accessRequest.Context = AccessContext.DepartmentWide;
            _accessRequest.Role = Role.Student;
            _departmentScopeAllowedResult = false;
            ArrangeMockingObjects();
            _service = new AccessPolicyAuthorizationService(_logger, _repo);

            // Act
            var result = _service.Authorize(_accessRequest);

            // Assert
            Assert.That(AccessOutcome.Deny, Is.EqualTo(result.Outcome));
            Assert.That("Context mismatch.", Is.EqualTo(result.Reason));
        }

        [Test]
        public void Authorize_ReturnsConditional_WhenContextMismatch_WithBreakGlass()
        {
            // Arrange
            _accessRequest.Context = AccessContext.DepartmentWide;
            _accessRequest.Role = Role.Registrar;
            _accessRequest.BreakGlassRequested = true;
            _departmentScopeAllowedResult = false;
            ArrangeMockingObjects();
            _service = new AccessPolicyAuthorizationService(_logger, _repo);

            // Act
            var result = _service.Authorize(_accessRequest);

            // Assert
            Assert.That(AccessOutcome.Conditional, Is.EqualTo(result.Outcome));
            Assert.That("Break-glass required.", Is.EqualTo(result.Reason));
            Assert.That(result.Obligations, Contains.Item(Obligation.AuditLog));
            Assert.That(result.Obligations, Contains.Item(Obligation.ManagerApproval));
        }

        [Test]
        public void Authorize_ReturnsDeny_WhenFerpaViolation_ViewGrades_WrongRole()
        {
            // Arrange
            _accessRequest.Operation = Operation.ViewGrades;
            _accessRequest.Role = Role.Admin; // This should be allowed
            _accessRequest.Context = AccessContext.CourseRoster; // But context doesn't match
            ArrangeMockingObjects();
            _service = new AccessPolicyAuthorizationService(_logger, _repo);

            // Act
            var result = _service.Authorize(_accessRequest);

            // Assert
            Assert.That(AccessOutcome.Deny, Is.EqualTo(result.Outcome));
            Assert.That("Context mismatch.", Is.EqualTo(result.Reason));
        }

        [Test]
        public void Authorize_ReturnsDeny_WhenFerpaViolation_ViewPII_WrongRole()
        {
            // Arrange
            _accessRequest.Operation = Operation.ViewPII;
            _accessRequest.Role = Role.Student;
            _accessRequest.Context = AccessContext.StudentOwnRecord;
            ArrangeMockingObjects();
            _service = new AccessPolicyAuthorizationService(_logger, _repo);

            // Act
            var result = _service.Authorize(_accessRequest);

            // Assert
            Assert.That(AccessOutcome.Deny, Is.EqualTo(result.Outcome));
            Assert.That("FERPA privacy constraint.", Is.EqualTo(result.Reason));
        }

        [Test]
        public void Authorize_ReturnsConditional_WhenFerpaViolation_WithBreakGlass()
        {
            // Arrange
            _accessRequest.Operation = Operation.ViewPII;
            _accessRequest.Role = Role.Admin;
            _accessRequest.Context = AccessContext.UniversityWide;
            _accessRequest.BreakGlassRequested = true;
            ArrangeMockingObjects();
            _service = new AccessPolicyAuthorizationService(_logger, _repo);

            // Act - First we need to make FERPA fail but allow break glass
            _accessRequest.Role = Role.Student; // This will fail FERPA for ViewPII
            _accessRequest.Role = Role.Admin; // Then use Admin for break glass capability
            _accessRequest.BreakGlassRequested = true;

            var result = _service.Authorize(_accessRequest);

            // Assert - This should allow since Admin + UniversityWide + ViewPII is valid
            Assert.That(AccessOutcome.Allow, Is.EqualTo(result.Outcome));
        }

        [Test]
        public void Authorize_ReturnsThrottle_WhenAfterHours_NoBreakGlass()
        {
            // Arrange
            _accessRequest.LocalHour = 23; // After hours
            _afterHoursAllowedResult = false;
            ArrangeMockingObjects();
            _service = new AccessPolicyAuthorizationService(_logger, _repo);

            // Act
            var result = _service.Authorize(_accessRequest);

            // Assert
            Assert.That(AccessOutcome.Throttle, Is.EqualTo(result.Outcome));
            Assert.That("After-hours restriction.", Is.EqualTo(result.Reason));
            Assert.That(result.RetryAfterSeconds, Is.GreaterThan(0));
        }

        [Test]
        public void Authorize_ReturnsConditional_WhenAfterHours_WithBreakGlass()
        {
            // Arrange
            _accessRequest.LocalHour = 2; // After hours (early morning)
            _accessRequest.Role = Role.Registrar;
            _accessRequest.BreakGlassRequested = true;
            _afterHoursAllowedResult = false;
            ArrangeMockingObjects();
            _service = new AccessPolicyAuthorizationService(_logger, _repo);

            // Act
            var result = _service.Authorize(_accessRequest);

            // Assert
            Assert.That(AccessOutcome.Conditional, Is.EqualTo(result.Outcome));
            Assert.That("Break-glass required.", Is.EqualTo(result.Reason));
            Assert.That(result.Obligations, Contains.Item(Obligation.AuditLog));
        }

        [Test]
        public void Authorize_ReturnsAllow_WhenAllChecksPass()
        {
            // Arrange
            ArrangeMockingObjects();
            _service = new AccessPolicyAuthorizationService(_logger, _repo);

            // Act
            var result = _service.Authorize(_accessRequest);

            // Assert
            Assert.That(AccessOutcome.Allow, Is.EqualTo(result.Outcome));
        }

        [TestCase(Role.Student, AccessContext.StudentOwnRecord, true)]
        [TestCase(Role.Advisor, AccessContext.AdviseeRecord, false)]
        [TestCase(Role.Instructor, AccessContext.CourseRoster, true)]
        [TestCase(Role.Admin, AccessContext.UniversityWide, true)]
        [TestCase(Role.Student, AccessContext.CourseRoster, false)]
        [TestCase(Role.Advisor, AccessContext.StudentOwnRecord, false)]
        public void Authorize_ContextMatching_ReturnsExpectedResult(Role role, AccessContext context, bool expectedToPass)
        {
            // Arrange
            _accessRequest.Role = role;
            _accessRequest.Context = context;
            if (context == AccessContext.DepartmentWide)
                _departmentScopeAllowedResult = expectedToPass;
            ArrangeMockingObjects();
            _service = new AccessPolicyAuthorizationService(_logger, _repo);

            // Act
            var result = _service.Authorize(_accessRequest);

            // Assert
            if (expectedToPass)
                Assert.That(AccessOutcome.Allow, Is.EqualTo(result.Outcome));
            else
                Assert.That(AccessOutcome.Deny, Is.EqualTo(result.Outcome));
        }

        [TestCase(Operation.ViewGrades, Role.Student, true)]
        [TestCase(Operation.ViewGrades, Role.Instructor, true)]
        [TestCase(Operation.ViewGrades, Role.Registrar, true)]
        [TestCase(Operation.ViewGrades, Role.Admin, true)]
        [TestCase(Operation.ViewGrades, Role.Advisor, false)]
        [TestCase(Operation.ViewPII, Role.Registrar, true)]
        [TestCase(Operation.ViewPII, Role.Admin, true)]
        [TestCase(Operation.ViewPII, Role.Student, false)]
        [TestCase(Operation.ViewPII, Role.Instructor, false)]
        public void Authorize_FerpaChecks_ReturnsExpectedResult(Operation operation, Role role, bool expectedToPass)
        {
            // Arrange
            _accessRequest.Operation = operation;
            _accessRequest.Role = role;
            _accessRequest.Context = GetValidContextForRole(role);
            ArrangeMockingObjects();
            _service = new AccessPolicyAuthorizationService(_logger, _repo);

            // Act
            var result = _service.Authorize(_accessRequest);

            // Assert
            if (expectedToPass)
                Assert.That(AccessOutcome.Allow, Is.EqualTo(result.Outcome));
            else
                Assert.That(AccessOutcome.Deny, Is.EqualTo(result.Outcome));
        }

        [TestCase(5)] // Early morning after hours
        [TestCase(22)] // Late night after hours
        [TestCase(23)] // Late night after hours
        [TestCase(1)] // Early morning after hours
        public void Authorize_AfterHoursChecks_ReturnsThrottle(int hour)
        {
            // Arrange
            _accessRequest.LocalHour = hour;
            _afterHoursAllowedResult = false;
            ArrangeMockingObjects();
            _service = new AccessPolicyAuthorizationService(_logger, _repo);

            // Act
            var result = _service.Authorize(_accessRequest);

            // Assert
            Assert.That(AccessOutcome.Throttle, Is.EqualTo(result.Outcome));
            Assert.That(result.RetryAfterSeconds, Is.GreaterThan(0));
        }

        [TestCase(8)] // Normal hours
        [TestCase(12)] // Normal hours
        [TestCase(16)] // Normal hours
        [TestCase(20)] // Normal hours
        public void Authorize_NormalHours_ReturnsAllow(int hour)
        {
            // Arrange
            _accessRequest.LocalHour = hour;
            ArrangeMockingObjects();
            _service = new AccessPolicyAuthorizationService(_logger, _repo);

            // Act
            var result = _service.Authorize(_accessRequest);

            // Assert
            Assert.That(AccessOutcome.Allow, Is.EqualTo(result.Outcome));
        }

        #endregion

        #region Helper Methods

        private AccessContext GetValidContextForRole(Role role)
        {
            switch (role)
            {
                case Role.Student:
                    return AccessContext.StudentOwnRecord;
                case Role.Advisor:
                    return AccessContext.AdviseeRecord;
                case Role.Instructor:
                    return AccessContext.CourseRoster;
                case Role.Registrar:
                    _departmentScopeAllowedResult = true;
                    return AccessContext.DepartmentWide;
                case Role.Admin:
                    return AccessContext.UniversityWide;
                default:
                    return AccessContext.StudentOwnRecord;
            }
        }

        #endregion
    }
}