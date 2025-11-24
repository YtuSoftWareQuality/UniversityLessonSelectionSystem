using System;
using System.Collections.Generic;
using UniversityLessonSelectionSystem.Domain.Enums;
using UniversityLessonSelectionSystem.Ports.AccessPolicy;

namespace UniversityLessonSelectionSystem.Infrastructure.AccessPolicy
{
    /// <summary>
    /// IAccessPolicyRepo arayüzünü, hafızada (in-memory) tutulan sabit kural setleriyle
    /// implemente eden örnek bir politika deposudur.
    /// 
    /// - Role → Operation izinleri
    /// - Role → Operation (mesai dışı) izinleri
    /// - Role → Department (department-wide scope) izinleri
    /// 
    /// gibi temel kuralları içerir. Eğitim amaçlıdır; gerçek sistemlerde veritabanı veya
    /// konfigürasyon tabanlı bir depoyla değiştirilebilir.
    /// </summary>
    public sealed class InMemoryAccessPolicyRepo : IAccessPolicyRepo
    {
        private readonly HashSet<Tuple<Role, Operation>> _roleOperationAllow;
        private readonly HashSet<Tuple<Role, Operation>> _afterHoursAllow;
        private readonly HashSet<Tuple<Role, Department>> _departmentScopeAllow;

        /// <summary>
        /// Varsayılan politika setlerini oluşturarak in-memory repo örneğini oluşturur.
        /// İstersen ctor’a harici konfigürasyon da enjekte edebilirsin.
        /// </summary>
        public InMemoryAccessPolicyRepo()
        {
            _roleOperationAllow = new HashSet<Tuple<Role, Operation>>();
            _afterHoursAllow = new HashSet<Tuple<Role, Operation>>();
            _departmentScopeAllow = new HashSet<Tuple<Role, Department>>();

            SeedRoleOperationPolicies();
            SeedAfterHoursPolicies();
            SeedDepartmentScopePolicies();
        }

        /// <summary>
        /// Verilen rol ve operasyon çifti için normal zamanda (mesai içi) erişim izni olup olmadığını döner.
        /// </summary>
        public bool RoleCan(Role role, Operation operation)
        {
            return _roleOperationAllow.Contains(Tuple.Create(role, operation));
        }

        /// <summary>
        /// Verilen rol ve operasyon çifti için mesai dışı (after-hours) erişim izni olup olmadığını döner.
        /// AccessPolicyAuthorizationService içindeki IsAfterHours kuralıyla birlikte çalışır.
        /// </summary>
        public bool AfterHoursAllowed(Role role, Operation operation)
        {
            return _afterHoursAllow.Contains(Tuple.Create(role, operation));
        }

        /// <summary>
        /// Verilen rol için, department-wide kapsamda işlem yapmaya izin verilip verilmediğini döner.
        /// Örneğin bir bölüm sekreterinin veya bölüm başkanının, bölüm çapında yetkisi olup olmadığını kontrol eder.
        /// </summary>
        public bool DepartmentScopeAllowed(Role role, Department department)
        {
            // Basit bir örnek: Department parametresini şimdilik ayırt etmiyor, sadece role bazlı değerlendiriyor.
            return _departmentScopeAllow.Contains(Tuple.Create(role, department));
        }

        #region Seed helpers

        /// <summary>
        /// Rol → Operation normal zaman izinlerini doldurur (örnek politika seti).
        /// </summary>
        private void SeedRoleOperationPolicies()
        {
            // Öğrenci kendi kayıtlarını görüp değiştirebilir, notlarını görebilir.
            _roleOperationAllow.Add(Tuple.Create(Role.Student, Operation.ViewOwnEnrollment));
            _roleOperationAllow.Add(Tuple.Create(Role.Student, Operation.EditOwnEnrollment));
            _roleOperationAllow.Add(Tuple.Create(Role.Student, Operation.ViewGrades));

            // Danışman, danışmanlığını yaptığı öğrencilerin kayıtlarını görebilir/düzenleyebilir.
            _roleOperationAllow.Add(Tuple.Create(Role.Advisor, Operation.ViewAdviseeEnrollment));
            _roleOperationAllow.Add(Tuple.Create(Role.Advisor, Operation.EditAdviseeEnrollment));
            _roleOperationAllow.Add(Tuple.Create(Role.Advisor, Operation.ViewGrades));

            // Eğitmen, verdiği derslerin listelerini ve notlarını yönetebilir.
            _roleOperationAllow.Add(Tuple.Create(Role.Instructor, Operation.ViewRoster));
            _roleOperationAllow.Add(Tuple.Create(Role.Instructor, Operation.EditGrades));

            // Öğrenci işleri (Registrar), çoğu akademik kaydı yönetebilir.
            _roleOperationAllow.Add(Tuple.Create(Role.Registrar, Operation.ViewTranscript));
            _roleOperationAllow.Add(Tuple.Create(Role.Registrar, Operation.EditTranscript));
            _roleOperationAllow.Add(Tuple.Create(Role.Registrar, Operation.PublishGrades));

            // Admin her şeyi yapabilir (örnek).
            foreach (Operation op in Enum.GetValues(typeof(Operation)))
            {
                _roleOperationAllow.Add(Tuple.Create(Role.Admin, op));
            }
        }

        /// <summary>
        /// Rol → Operation mesai dışı (after-hours) izinlerini doldurur (örnek politika seti).
        /// </summary>
        private void SeedAfterHoursPolicies()
        {
            // Öğrenci mesai dışında kendi kayıt bilgilerini görüntüleyebilir fakat değiştiremez.
            _afterHoursAllow.Add(Tuple.Create(Role.Student, Operation.ViewOwnEnrollment));
            _afterHoursAllow.Add(Tuple.Create(Role.Student, Operation.ViewGrades));

            // Eğitmen ve danışmanlar belirli okuma işlemlerini after-hours yapabilsin.
            _afterHoursAllow.Add(Tuple.Create(Role.Instructor, Operation.ViewRoster));
            _afterHoursAllow.Add(Tuple.Create(Role.Advisor, Operation.ViewAdviseeEnrollment));

            // Registrar ve Admin için çoğu işlem after-hours serbest (örnek).
            foreach (Operation op in Enum.GetValues(typeof(Operation)))
            {
                _afterHoursAllow.Add(Tuple.Create(Role.Registrar, op));
                _afterHoursAllow.Add(Tuple.Create(Role.Admin, op));
            }
        }

        /// <summary>
        /// Rol → Department department-wide scope izinlerini doldurur (örnek politika seti).
        /// </summary>
        private void SeedDepartmentScopePolicies()
        {
            // Bölüm başkanı veya departman rolü gibi özel bir rolün olduğunu varsaymıyoruz;
            // örnek olarak Instructor ve Advisor için department-level read izinleri verilebilir.
            foreach (Department dept in Enum.GetValues(typeof(Department)))
            {
                _departmentScopeAllow.Add(Tuple.Create(Role.Advisor, dept));
                _departmentScopeAllow.Add(Tuple.Create(Role.Registrar, dept));
                _departmentScopeAllow.Add(Tuple.Create(Role.Admin, dept));
            }
        }

        #endregion
    }
}
